using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LlmInteractionLogger.Models;
using LlmInteractionLogger.Options;
using LlmInteractionLogger.Telemetry;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace LlmInteractionLogger.Services;

public interface IInteractionIngestionService
{
    /// <summary>Captures an interaction posted as an <c>application/json</c> envelope.</summary>
    Task<CaptureResult> IngestJsonAsync(Stream body, CancellationToken cancellationToken);

    /// <summary>Captures an interaction posted as <c>multipart/form-data</c>: an <c>interaction</c> JSON field plus file parts.</summary>
    Task<CaptureResult> IngestMultipartAsync(Stream body, string boundary, CancellationToken cancellationToken);

    /// <summary>
    /// Captures one half of an interaction (the request or the response) whose body is the raw provider payload,
    /// e.g. forwarded unchanged by an API Management inbound/outbound policy. Interaction details come from headers.
    /// </summary>
    Task<CaptureResult> IngestRawAsync(Stream body, RawCapture raw, CancellationToken cancellationToken);
}

public sealed class InteractionIngestionService(
    IBlobArchive archive,
    ILlmTelemetry telemetry,
    IOptions<LlmLoggingOptions> options,
    TimeProvider timeProvider,
    ILogger<InteractionIngestionService> logger) : IInteractionIngestionService
{
    public const string InteractionFieldName = "interaction";

    private static readonly FileExtensionContentTypeProvider ExtensionTypes = new();
    private static readonly JsonDocumentOptions DocumentOptions = new() { MaxDepth = 256 };

    private readonly LlmLoggingOptions _options = options.Value;

    public async Task<CaptureResult> IngestJsonAsync(Stream body, CancellationToken cancellationToken)
    {
        var capture = NewCapture();
        try
        {
            var root = await ReadJsonAsync(body, cancellationToken).ConfigureAwait(false);
            return await CompleteAsync(capture, root, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await CleanupAsync(capture).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<CaptureResult> IngestMultipartAsync(Stream body, string boundary, CancellationToken cancellationToken)
    {
        var capture = NewCapture();
        try
        {
            JsonNode? root = null;
            var reader = new MultipartReader(boundary, body);
            MultipartSection? section;
            while ((section = await reader.ReadNextSectionAsync(cancellationToken).ConfigureAwait(false)) is not null)
            {
                var disposition = section.GetContentDispositionHeader()
                    ?? throw new InvalidPayloadException("Every multipart section must have a Content-Disposition header.");

                if (disposition.IsFileDisposition())
                {
                    await StoreMultipartFileAsync(capture, section, disposition, cancellationToken).ConfigureAwait(false);
                }
                else if (string.Equals(disposition.Name.Value, InteractionFieldName, StringComparison.OrdinalIgnoreCase))
                {
                    if (root is not null)
                    {
                        throw new InvalidPayloadException($"Only one '{InteractionFieldName}' field is allowed.");
                    }

                    root = await ReadJsonAsync(section.Body, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    logger.LogWarning("Ignoring unexpected multipart form field {FieldName} in capture {CaptureId}.", disposition.Name.Value, capture.Id);
                }
            }

            if (root is null)
            {
                throw new InvalidPayloadException($"The multipart request must contain an '{InteractionFieldName}' field with the interaction JSON.");
            }

            return await CompleteAsync(capture, root, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException && !cancellationToken.IsCancellationRequested)
        {
            await CleanupAsync(capture).ConfigureAwait(false);
            throw new InvalidPayloadException($"The multipart body is malformed or truncated: {ex.Message}");
        }
        catch
        {
            await CleanupAsync(capture).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<CaptureResult> IngestRawAsync(Stream body, RawCapture raw, CancellationToken cancellationToken)
    {
        var capture = NewCapture();
        try
        {
            if (string.Equals(raw.BodyEncoding, "base64", StringComparison.OrdinalIgnoreCase))
            {
                body = await DecodeBase64Async(body, cancellationToken).ConfigureAwait(false);
            }
            else if (!string.IsNullOrEmpty(raw.BodyEncoding))
            {
                throw new InvalidPayloadException($"Unsupported body encoding '{raw.BodyEncoding}'. Only 'base64' is supported.");
            }

            var payload = await ReadRawPayloadAsync(capture, body, raw, cancellationToken).ConfigureAwait(false);
            var root = RawEnvelope(raw, payload);
            return await CompleteAsync(capture, root, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException && !cancellationToken.IsCancellationRequested)
        {
            await CleanupAsync(capture).ConfigureAwait(false);
            throw new InvalidPayloadException($"The body is malformed or truncated: {ex.Message}");
        }
        catch
        {
            await CleanupAsync(capture).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<JsonNode> ReadRawPayloadAsync(Capture capture, Stream body, RawCapture raw, CancellationToken cancellationToken)
    {
        MediaTypeHeaderValue.TryParse(raw.ContentType, out var media);
        var mediaType = media?.MediaType.Value?.ToLowerInvariant() ?? string.Empty;

        if (mediaType == "multipart/form-data")
        {
            var boundary = HeaderUtilities.RemoveQuotes(media!.Boundary).Value;
            if (string.IsNullOrWhiteSpace(boundary))
            {
                throw new InvalidPayloadException("The multipart body has no boundary.");
            }

            return await ReadProviderMultipartAsync(capture, body, boundary, cancellationToken).ConfigureAwait(false);
        }

        if (mediaType == "application/json" || mediaType.EndsWith("+json", StringComparison.Ordinal))
        {
            return await ReadJsonAsync(body, cancellationToken).ConfigureAwait(false);
        }

        using var buffer = await ReadBufferAsync(body, _options.MaxAttachmentBytes, "body", cancellationToken).ConfigureAwait(false);
        if (buffer.Length == 0)
        {
            return JsonValue.Create(string.Empty);
        }

        if (mediaType == "text/event-stream")
        {
            return ServerSentEventsParser.Parse(Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length));
        }

        if (media is null && TryParseJson(buffer, out var sniffed))
        {
            return sniffed;
        }

        if (mediaType.StartsWith("text/", StringComparison.Ordinal)
            || mediaType is "application/x-www-form-urlencoded" or "application/xml" or "application/x-ndjson")
        {
            if (buffer.Length > _options.MaxJsonBytes)
            {
                throw new PayloadTooLargeException($"The text body exceeds the {_options.MaxJsonBytes} byte limit.");
            }

            return JsonValue.Create(Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length));
        }

        // Binary body (e.g. text-to-speech audio or an image upload): archive it whole.
        var contentType = mediaType.Length > 0 ? ContentTypes.Normalize(mediaType) : ContentTypes.OctetStream;
        if (contentType == ContentTypes.OctetStream)
        {
            contentType = ContentTypes.Sniff(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)) ?? contentType;
        }

        var attachmentId = capture.NextAttachmentId();
        await StoreAsync(capture, attachmentId, AttachmentSource.RawBody, $"$.{raw.Phase}", null, contentType, buffer, cancellationToken)
            .ConfigureAwait(false);
        return new JsonObject
        {
            ["body"] = AttachmentReference.For(attachmentId),
            ["contentType"] = contentType,
            ["sizeBytes"] = buffer.Length,
        };
    }

    private async Task<JsonObject> ReadProviderMultipartAsync(Capture capture, Stream body, string boundary, CancellationToken cancellationToken)
    {
        var fields = new JsonObject();
        var reader = new MultipartReader(boundary, body);
        MultipartSection? section;
        while ((section = await reader.ReadNextSectionAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            var disposition = section.GetContentDispositionHeader()
                ?? throw new InvalidPayloadException("Every multipart section must have a Content-Disposition header.");
            var name = HeaderUtilities.RemoveQuotes(disposition.Name).Value ?? "field";

            JsonNode value;
            if (disposition.IsFileDisposition())
            {
                var before = capture.Attachments.Count;
                await StoreMultipartFileAsync(capture, section, disposition, cancellationToken).ConfigureAwait(false);
                value = AttachmentReference.For(capture.Attachments[before].Id);
            }
            else
            {
                using var buffer = await ReadBufferAsync(section.Body, _options.MaxJsonBytes, $"form field '{name}'", cancellationToken).ConfigureAwait(false);
                value = JsonValue.Create(Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length))!;
            }

            switch (fields[name])
            {
                case null:
                    fields[name] = value;
                    break;
                case JsonArray values:
                    values.Add(value);
                    break;
                case var existing:
                    fields.Remove(name);
                    fields[name] = new JsonArray(existing, value);
                    break;
            }
        }

        return fields;
    }

    private static JsonObject RawEnvelope(RawCapture raw, JsonNode payload)
    {
        var root = new JsonObject();
        void Set(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                root[key] = value;
            }
        }

        Set("interactionId", raw.InteractionId);
        Set("conversationId", raw.ConversationId);
        Set("userId", raw.UserId);
        Set("application", raw.Application);
        Set("provider", raw.Provider);
        Set("operation", raw.Operation);
        if (raw.LatencyMs is { } latency)
        {
            root["latencyMs"] = latency;
        }

        var metadata = new JsonObject();
        foreach (var (key, value) in raw.Metadata)
        {
            metadata[key] = value;
        }

        if (raw.StatusCode is { } statusCode)
        {
            metadata["http_status_code"] = statusCode.ToString(CultureInfo.InvariantCulture);
        }

        if (metadata.Count > 0)
        {
            root["metadata"] = metadata;
        }

        if (raw.Phase == RawCapture.RequestPhase)
        {
            root["request"] = payload;
            // Azure OpenAI carries the deployment in the URL, not the body; the header fills that gap.
            Set("model", raw.Model ?? ModelOf(payload));
        }
        else
        {
            var failed = raw.StatusCode >= 400;
            root[failed ? "error" : "response"] = payload;
            root["status"] = failed ? "error" : "success";
            Set("model", ModelOf(payload) ?? raw.Model);
        }

        return root;
    }

    private static string? ModelOf(JsonNode payload) =>
        (payload as JsonObject)?["model"] is JsonValue value && value.TryGetValue(out string? model) ? model : null;

    private async Task<MemoryStream> DecodeBase64Async(Stream body, CancellationToken cancellationToken)
    {
        var limit = (Math.Max(_options.MaxAttachmentBytes, _options.MaxJsonBytes) + 2) / 3 * 4 + 4;
        using var encoded = await ReadBufferAsync(body, limit, "base64 body", cancellationToken).ConfigureAwait(false);
        var text = Encoding.ASCII.GetString(encoded.GetBuffer(), 0, (int)encoded.Length).Trim();
        try
        {
            return new MemoryStream(Convert.FromBase64String(text), writable: false);
        }
        catch (FormatException)
        {
            throw new InvalidPayloadException("The body is not valid base64.");
        }
    }

    private static bool TryParseJson(MemoryStream buffer, out JsonNode node)
    {
        node = null!;
        var first = buffer.GetBuffer().AsSpan(0, (int)buffer.Length).TrimStart(" \t\r\n"u8);
        if (first.IsEmpty || first[0] is not ((byte)'{' or (byte)'['))
        {
            return false;
        }

        try
        {
            node = JsonNode.Parse(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), documentOptions: DocumentOptions)!;
            return node is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<CaptureResult> CompleteAsync(Capture capture, JsonNode node, CancellationToken cancellationToken)
    {
        if (node is not JsonObject root)
        {
            throw new InvalidPayloadException("The interaction must be a JSON object.");
        }

        if (root["request"] is null && root["response"] is null && root["error"] is null)
        {
            throw new InvalidPayloadException("The interaction must contain a 'request', 'response' and/or 'error' property.");
        }

        var extractor = new BinaryContentExtractor(_options.MinInlineBinaryChars, _options.MaxAttachments, _options.MaxAttachmentBytes);
        var inline = extractor.Extract(root, capture.NextAttachmentId, capture.Attachments.Count);
        foreach (var binary in inline)
        {
            using var content = new MemoryStream(binary.Data, writable: false);
            await StoreAsync(capture, binary.Id, AttachmentSource.InlineJson, binary.JsonPath, binary.FileName, binary.ContentType, content, cancellationToken)
                .ConfigureAwait(false);
        }

        var summary = InteractionSummaryBuilder.Build(capture.Id, root);

        Uri? archiveUri = null;
        if (_options.ArchiveInteractionJson)
        {
            archiveUri = await ArchiveInteractionAsync(capture, summary, root, cancellationToken).ConfigureAwait(false);
        }

        var contents = new List<ContentPart>();
        AddContent(contents, "prompt", root["request"]);
        AddContent(contents, "response", root["response"]);
        AddContent(contents, "error", root["error"]);
        AddContent(contents, "attachments", root["attachments"]);

        var chunks = telemetry.Track(summary, capture.Attachments, contents, archiveUri);

        return new CaptureResult
        {
            CaptureId = capture.Id,
            InteractionId = summary.InteractionId,
            Attachments = capture.Attachments,
            ContentChunks = chunks,
            ArchivedInteractionUri = archiveUri,
        };
    }

    private async Task StoreMultipartFileAsync(Capture capture, MultipartSection section, ContentDispositionHeaderValue disposition, CancellationToken cancellationToken)
    {
        if (capture.Attachments.Count >= _options.MaxAttachments)
        {
            throw new PayloadTooLargeException($"The interaction contains more than {_options.MaxAttachments} attachments.");
        }

        var fileName = HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value;
        var contentType = ContentTypes.Normalize(section.ContentType);
        if (contentType == ContentTypes.OctetStream && fileName is not null && ExtensionTypes.TryGetContentType(fileName, out var byExtension))
        {
            contentType = byExtension;
        }

        var field = HeaderUtilities.RemoveQuotes(disposition.Name).Value ?? "file";
        await StoreAsync(capture, capture.NextAttachmentId(), AttachmentSource.Multipart, $"multipart:{field}", fileName, contentType, section.Body, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task StoreAsync(
        Capture capture,
        string attachmentId,
        AttachmentSource source,
        string location,
        string? fileName,
        string contentType,
        Stream content,
        CancellationToken cancellationToken)
    {
        var container = _options.AttachmentsContainer;
        var blobName = $"{capture.Prefix}/{attachmentId}{BlobSuffix(fileName, contentType)}";
        var metadata = new Dictionary<string, string>
        {
            ["capture_id"] = capture.Id,
            ["attachment_id"] = attachmentId,
            ["source"] = source.ToString(),
            ["location"] = MetadataValue(location),
        };
        if (fileName is not null)
        {
            metadata["file_name"] = MetadataValue(fileName);
        }

        capture.Blobs.Add((container, blobName));
        var stored = await archive.UploadAsync(container, blobName, content, contentType, metadata, _options.MaxAttachmentBytes, cancellationToken)
            .ConfigureAwait(false);

        capture.Attachments.Add(new StoredAttachment
        {
            Id = attachmentId,
            Source = source,
            Location = location,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = stored.SizeBytes,
            Sha256 = stored.Sha256,
            Container = container,
            BlobName = blobName,
            BlobUri = stored.Uri,
        });
    }

    private async Task<Uri> ArchiveInteractionAsync(Capture capture, InteractionSummary summary, JsonObject root, CancellationToken cancellationToken)
    {
        var document = new JsonObject
        {
            ["captureId"] = capture.Id,
            ["capturedAt"] = capture.CapturedAt.ToString("O", CultureInfo.InvariantCulture),
            ["attachments"] = JsonSerializer.SerializeToNode(capture.Attachments),
            ["interaction"] = root.DeepClone(),
        };

        var container = _options.InteractionsContainer;
        var blobName = $"{capture.Prefix}/interaction.json";
        var metadata = new Dictionary<string, string> { ["capture_id"] = capture.Id };
        if (summary.InteractionId is not null)
        {
            metadata["interaction_id"] = MetadataValue(summary.InteractionId);
        }

        capture.Blobs.Add((container, blobName));
        using var content = new MemoryStream(Encoding.UTF8.GetBytes(document.ToJsonString()), writable: false);
        var stored = await archive.UploadAsync(container, blobName, content, "application/json", metadata, long.MaxValue, cancellationToken)
            .ConfigureAwait(false);
        return stored.Uri;
    }

    private static async Task<MemoryStream> ReadBufferAsync(Stream body, long limit, string what, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > limit)
            {
                buffer.Dispose();
                throw new PayloadTooLargeException($"The {what} exceeds the {limit} byte limit.");
            }

            buffer.Write(chunk, 0, read);
        }

        buffer.Position = 0;
        return buffer;
    }

    private async Task<JsonNode> ReadJsonAsync(Stream body, CancellationToken cancellationToken)
    {
        using var buffer = await ReadBufferAsync(body, _options.MaxJsonBytes, "interaction JSON", cancellationToken).ConfigureAwait(false);
        if (buffer.Length == 0)
        {
            throw new InvalidPayloadException("The request body is empty.");
        }

        buffer.Position = 0;
        try
        {
            return JsonNode.Parse(buffer, documentOptions: DocumentOptions)
                ?? throw new InvalidPayloadException("The interaction JSON must not be null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidPayloadException($"The interaction is not valid JSON: {ex.Message}");
        }
    }

    private async Task CleanupAsync(Capture capture)
    {
        foreach (var (container, blobName) in capture.Blobs)
        {
            try
            {
                await archive.DeleteAsync(container, blobName, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete blob {Container}/{BlobName} after a failed capture {CaptureId}.", container, blobName, capture.Id);
            }
        }
    }

    private static void AddContent(List<ContentPart> contents, string kind, JsonNode? node)
    {
        if (node is not null)
        {
            contents.Add(new ContentPart(kind, node.ToJsonString()));
        }
    }

    private static string BlobSuffix(string? fileName, string contentType)
    {
        var safe = new StringBuilder();
        foreach (var c in Path.GetFileName(fileName ?? string.Empty))
        {
            if (safe.Length == 100)
            {
                break;
            }

            if (char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_')
            {
                safe.Append(c);
            }
        }

        var name = safe.ToString().Trim('.');
        return name.Length > 0 ? $"-{name}" : ContentTypes.ExtensionFor(contentType);
    }

    private static string MetadataValue(string value)
    {
        var escaped = Uri.EscapeDataString(value);
        return escaped.Length <= 1024 ? escaped : escaped[..1024];
    }

    private Capture NewCapture()
    {
        var now = timeProvider.GetUtcNow();
        var id = Guid.CreateVersion7(now).ToString("N");
        return new Capture(id, now, $"{now:yyyy'/'MM'/'dd}/{id}");
    }

    private sealed class Capture(string id, DateTimeOffset capturedAt, string prefix)
    {
        private int _attachmentCounter;

        public string Id { get; } = id;
        public DateTimeOffset CapturedAt { get; } = capturedAt;
        public string Prefix { get; } = prefix;
        public List<StoredAttachment> Attachments { get; } = [];
        public List<(string Container, string BlobName)> Blobs { get; } = [];

        public string NextAttachmentId() => $"att-{++_attachmentCounter:D3}";
    }
}
