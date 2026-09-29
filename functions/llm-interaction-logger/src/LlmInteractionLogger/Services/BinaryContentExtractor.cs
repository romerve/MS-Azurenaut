using System.Buffers.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LlmInteractionLogger.Models;

namespace LlmInteractionLogger.Services;

public sealed record ExtractedBinary(string Id, string JsonPath, byte[] Data, string ContentType, string? FileName);

/// <summary>
/// Walks an interaction JSON document, removes inline binary payloads (data URIs and base64 fields used by
/// OpenAI / Azure OpenAI, Anthropic, Gemini and similar APIs) and replaces each one with an
/// <c>llm-attachment://{id}</c> reference so the remaining text can be logged in full.
/// </summary>
public sealed partial class BinaryContentExtractor(int minInlineBinaryChars, int maxAttachments, long maxAttachmentBytes)
{
    private static readonly HashSet<string> BinaryFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "data", "b64_json", "base64", "bytes", "file_data", "partial_image_b64",
        "image_base64", "audio_base64", "video_base64", "image_data", "audio_data", "video_data",
    };

    private static readonly string[] MimeKeys = ["mimeType", "mime_type", "media_type", "mediaType", "contentType", "content_type"];

    private static readonly string[] NameKeys = ["filename", "fileName", "file_name", "name", "displayName", "display_name"];

    [GeneratedRegex(@"^data:(?<mime>[^;,]*)(?<params>(;[^;,]*)*?);base64,", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DataUriPrefix();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex SimpleIdentifier();

    public IReadOnlyList<ExtractedBinary> Extract(JsonNode root, Func<string> nextId, int existingAttachments = 0)
    {
        var found = new List<ExtractedBinary>();
        Visit(root, "$", found, nextId, existingAttachments);
        return found;
    }

    private void Visit(JsonNode? node, string path, List<ExtractedBinary> found, Func<string> nextId, int existing)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, child) in obj.ToList())
                {
                    var childPath = SimpleIdentifier().IsMatch(key) ? $"{path}.{key}" : $"{path}['{key.Replace("'", "\\'")}']";
                    if (child is JsonValue value && value.TryGetValue(out string? text))
                    {
                        var binary = TryExtract(text, key, obj, childPath, found, nextId, existing);
                        if (binary is not null)
                        {
                            obj[key] = AttachmentReference.For(binary.Id);
                        }
                    }
                    else
                    {
                        Visit(child, childPath, found, nextId, existing);
                    }
                }
                break;

            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    var childPath = $"{path}[{i}]";
                    if (array[i] is JsonValue value && value.TryGetValue(out string? text))
                    {
                        var binary = TryExtract(text, propertyName: null, siblings: null, childPath, found, nextId, existing);
                        if (binary is not null)
                        {
                            array[i] = AttachmentReference.For(binary.Id);
                        }
                    }
                    else
                    {
                        Visit(array[i], childPath, found, nextId, existing);
                    }
                }
                break;
        }
    }

    private ExtractedBinary? TryExtract(
        string text,
        string? propertyName,
        JsonObject? siblings,
        string path,
        List<ExtractedBinary> found,
        Func<string> nextId,
        int existing)
    {
        string? declaredType;
        ReadOnlySpan<char> payload;

        var dataUri = DataUriPrefix().Match(text);
        if (dataUri.Success)
        {
            declaredType = dataUri.Groups["mime"].Value;
            payload = text.AsSpan(dataUri.Length);
        }
        else if (propertyName is not null && BinaryFieldNames.Contains(propertyName) && text.Length > 0)
        {
            // A sibling MIME/format hint marks the value as binary; otherwise require a minimum length to avoid false positives.
            declaredType = FindMimeType(siblings);
            if (declaredType is null && text.Length < minInlineBinaryChars)
            {
                return null;
            }

            // Base64 may be line-wrapped but never contains spaces; this keeps prose out even if it is base64-alphabet only.
            if (text.Contains(' '))
            {
                return null;
            }

            payload = text.AsSpan();
        }
        else
        {
            return null;
        }

        if (!Base64.IsValid(payload, out var decodedLength))
        {
            return null;
        }

        if (decodedLength > maxAttachmentBytes)
        {
            throw new PayloadTooLargeException($"Inline binary at {path} is {decodedLength} bytes; the limit is {maxAttachmentBytes} bytes.");
        }

        if (existing + found.Count >= maxAttachments)
        {
            throw new PayloadTooLargeException($"The interaction contains more than {maxAttachments} attachments.");
        }

        var bytes = Convert.FromBase64String(payload.ToString());
        var binary = new ExtractedBinary(
            nextId(),
            path,
            bytes,
            ContentTypes.Resolve(declaredType, bytes.AsSpan(0, Math.Min(bytes.Length, 64))),
            FindString(siblings, NameKeys));
        found.Add(binary);
        return binary;
    }

    private static string? FindMimeType(JsonObject? siblings) =>
        FindString(siblings, MimeKeys) ?? ContentTypes.FromAudioFormat(FindString(siblings, ["format"]));

    private static string? FindString(JsonObject? obj, string[] keys)
    {
        if (obj is null)
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (obj[key] is JsonValue value && value.TryGetValue(out string? text) && !string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }
}
