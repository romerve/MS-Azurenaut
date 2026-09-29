using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using LlmInteractionLogger.Models;
using Microsoft.Extensions.Logging;

namespace LlmInteractionLogger.Telemetry;

/// <summary>A piece of interaction text (for example the prompt or the response) to log in full.</summary>
public sealed record ContentPart(string Kind, string Content);

public interface ILlmTelemetry
{
    /// <returns>Number of trace chunks written per content kind.</returns>
    IReadOnlyDictionary<string, int> Track(
        InteractionSummary summary,
        IReadOnlyList<StoredAttachment> attachments,
        IReadOnlyList<ContentPart> contents,
        Uri? archivedInteractionUri);
}

/// <summary>
/// Writes interaction telemetry through OpenTelemetry so the Azure Monitor exporter lands it in Application Insights:
/// <list type="bullet">
/// <item><c>customEvents</c> "LlmInteraction" (one per call) and "LlmAttachment" (one per binary blob).</item>
/// <item><c>traces</c> "LlmInteractionContent" holding the full prompt/response JSON split into ordered chunks.</item>
/// <item><c>customMetrics</c> for interactions, token usage, latency and attachment volume.</item>
/// </list>
/// </summary>
public sealed class LlmTelemetry : ILlmTelemetry
{
    public const string MeterName = "LlmInteractionLogger";
    public const string InteractionEventName = "LlmInteraction";
    public const string AttachmentEventName = "LlmAttachment";
    public const string ContentTraceName = "LlmInteractionContent";

    // Attribute recognized by the Azure Monitor exporter to route a log record to the customEvents table.
    public const string CustomEventAttribute = "microsoft.custom_event.name";

    private const int MaxMetadataEntries = 50;
    private const int MaxPropertyLength = 8192;

    private readonly ILogger<LlmTelemetry> _logger;
    private readonly int _chunkSize;
    private readonly Counter<long> _interactions;
    private readonly Histogram<long> _tokenUsage;
    private readonly Histogram<double> _duration;
    private readonly Counter<long> _attachments;
    private readonly Counter<long> _attachmentBytes;

    public LlmTelemetry(ILogger<LlmTelemetry> logger, IMeterFactory meterFactory, int chunkSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 100);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(chunkSize, MaxPropertyLength);

        _logger = logger;
        _chunkSize = chunkSize;
        var meter = meterFactory.Create(MeterName);
        _interactions = meter.CreateCounter<long>("llm.interactions", "{interaction}", "LLM interactions captured.");
        _tokenUsage = meter.CreateHistogram<long>("gen_ai.client.token.usage", "{token}", "Tokens used per LLM interaction.");
        _duration = meter.CreateHistogram<double>("gen_ai.client.operation.duration", "s", "Reported LLM call duration.");
        _attachments = meter.CreateCounter<long>("llm.attachments", "{attachment}", "Binary attachments offloaded to blob storage.");
        _attachmentBytes = meter.CreateCounter<long>("llm.attachment.bytes", "By", "Bytes of binary attachments offloaded to blob storage.");
    }

    public IReadOnlyDictionary<string, int> Track(
        InteractionSummary summary,
        IReadOnlyList<StoredAttachment> attachments,
        IReadOnlyList<ContentPart> contents,
        Uri? archivedInteractionUri)
    {
        var chunkCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var part in contents)
        {
            chunkCounts[part.Kind] = WriteContent(summary, part.Kind, part.Content);
        }

        foreach (var attachment in attachments)
        {
            WriteAttachment(summary, attachment);
        }

        WriteInteraction(summary, attachments, contents, chunkCounts, archivedInteractionUri);
        RecordMetrics(summary, attachments);

        return chunkCounts;
    }

    public static IReadOnlyList<string> Chunk(string text, int size)
    {
        var chunks = new List<string>();
        var start = 0;
        while (start < text.Length)
        {
            var length = Math.Min(size, text.Length - start);
            // Never split a UTF-16 surrogate pair across two records.
            if (start + length < text.Length && char.IsHighSurrogate(text[start + length - 1]))
            {
                length--;
            }

            chunks.Add(text.Substring(start, length));
            start += length;
        }

        return chunks;
    }

    private int WriteContent(InteractionSummary summary, string kind, string content)
    {
        var chunks = Chunk(content, _chunkSize);
        for (var i = 0; i < chunks.Count; i++)
        {
            var attributes = Correlation(summary);
            attributes.Add(new("llm.content.kind", kind));
            attributes.Add(new("llm.content.chunk_index", i));
            attributes.Add(new("llm.content.chunk_count", chunks.Count));
            attributes.Add(new("llm.content", chunks[i]));
            attributes.Add(new("llm.trace_type", ContentTraceName));

            Write(
                "LLM {llm.content.kind} content chunk {llm.content.chunk_index} of {llm.content.chunk_count}",
                $"LLM {kind} content chunk {i} of {chunks.Count}",
                attributes);
        }

        return chunks.Count;
    }

    private void WriteAttachment(InteractionSummary summary, StoredAttachment attachment)
    {
        var attributes = Correlation(summary);
        attributes.Add(new(CustomEventAttribute, AttachmentEventName));
        attributes.Add(new("llm.attachment.id", attachment.Id));
        attributes.Add(new("llm.attachment.reference", attachment.Reference));
        attributes.Add(new("llm.attachment.source", attachment.Source.ToString()));
        attributes.Add(new("llm.attachment.location", attachment.Location));
        AddIfPresent(attributes, "llm.attachment.file_name", attachment.FileName);
        attributes.Add(new("llm.attachment.content_type", attachment.ContentType));
        attributes.Add(new("llm.attachment.size_bytes", attachment.SizeBytes));
        attributes.Add(new("llm.attachment.sha256", attachment.Sha256));
        attributes.Add(new("llm.attachment.blob_container", attachment.Container));
        attributes.Add(new("llm.attachment.blob_name", attachment.BlobName));
        attributes.Add(new("llm.attachment.blob_uri", attachment.BlobUri.ToString()));

        Write(AttachmentEventName, AttachmentEventName, attributes);
    }

    private void WriteInteraction(
        InteractionSummary summary,
        IReadOnlyList<StoredAttachment> attachments,
        IReadOnlyList<ContentPart> contents,
        IReadOnlyDictionary<string, int> chunkCounts,
        Uri? archivedInteractionUri)
    {
        var attributes = Correlation(summary);
        attributes.Add(new(CustomEventAttribute, InteractionEventName));
        AddIfPresent(attributes, "gen_ai.conversation.id", summary.ConversationId);
        AddIfPresent(attributes, "enduser.id", summary.UserId);
        AddIfPresent(attributes, "llm.application", summary.Application);
        AddIfPresent(attributes, "gen_ai.provider.name", summary.Provider);
        AddIfPresent(attributes, "gen_ai.request.model", summary.Model);
        AddIfPresent(attributes, "gen_ai.operation.name", summary.Operation);
        AddIfPresent(attributes, "llm.status", summary.Status);
        AddIfPresent(attributes, "gen_ai.response.finish_reasons", summary.FinishReason);
        AddIfPresent(attributes, "llm.started_at", summary.StartedAt?.ToString("O", CultureInfo.InvariantCulture));
        AddIfPresent(attributes, "llm.completed_at", summary.CompletedAt?.ToString("O", CultureInfo.InvariantCulture));
        if (summary.LatencyMs is { } latency) attributes.Add(new("llm.latency_ms", latency));
        if (summary.Usage.InputTokens is { } input) attributes.Add(new("gen_ai.usage.input_tokens", input));
        if (summary.Usage.OutputTokens is { } output) attributes.Add(new("gen_ai.usage.output_tokens", output));
        if (summary.Usage.TotalTokens is { } total) attributes.Add(new("llm.usage.total_tokens", total));
        foreach (var part in contents)
        {
            attributes.Add(new($"llm.{part.Kind}_chars", part.Content.Length));
            attributes.Add(new($"llm.{part.Kind}_chunks", chunkCounts[part.Kind]));
        }

        attributes.Add(new("llm.attachment_count", attachments.Count));
        attributes.Add(new("llm.attachment_bytes", attachments.Sum(a => a.SizeBytes)));
        if (attachments.Count > 0)
        {
            attributes.Add(new("llm.attachment_ids", Truncate(string.Join(',', attachments.Select(a => a.Id)))));
            attributes.Add(new("llm.attachment_content_types", Truncate(string.Join(',', attachments.Select(a => a.ContentType).Distinct()))));
        }

        AddIfPresent(attributes, "llm.archive_uri", archivedInteractionUri?.ToString());

        foreach (var (key, value) in summary.Metadata.Take(MaxMetadataEntries))
        {
            attributes.Add(new($"llm.metadata.{key}", Truncate(value)));
        }

        Write(InteractionEventName, InteractionEventName, attributes);
    }

    private void RecordMetrics(InteractionSummary summary, IReadOnlyList<StoredAttachment> attachments)
    {
        var provider = summary.Provider ?? "unknown";
        var model = summary.Model ?? "unknown";
        var tags = new TagList
        {
            { "gen_ai.provider.name", provider },
            { "gen_ai.request.model", model },
            { "llm.status", summary.Status ?? "unknown" },
        };

        // A request-only capture is followed by its response capture; count the interaction once.
        if (summary.Phase != "request")
        {
            _interactions.Add(1, tags);
        }

        if (summary.Usage.InputTokens is { } input)
        {
            _tokenUsage.Record(input, new TagList { { "gen_ai.provider.name", provider }, { "gen_ai.request.model", model }, { "gen_ai.token.type", "input" } });
        }

        if (summary.Usage.OutputTokens is { } output)
        {
            _tokenUsage.Record(output, new TagList { { "gen_ai.provider.name", provider }, { "gen_ai.request.model", model }, { "gen_ai.token.type", "output" } });
        }

        if (summary.LatencyMs is { } latency)
        {
            _duration.Record(latency / 1000d, tags);
        }

        foreach (var attachment in attachments)
        {
            var slash = attachment.ContentType.IndexOf('/');
            var attachmentTags = new TagList
            {
                { "llm.attachment.category", slash > 0 ? attachment.ContentType[..slash] : "unknown" },
                { "llm.attachment.source", attachment.Source.ToString() },
            };
            _attachments.Add(1, attachmentTags);
            _attachmentBytes.Add(attachment.SizeBytes, attachmentTags);
        }
    }

    private static List<KeyValuePair<string, object?>> Correlation(InteractionSummary summary)
    {
        var attributes = new List<KeyValuePair<string, object?>>
        {
            new("llm.capture_id", summary.CaptureId),
            new("llm.phase", summary.Phase),
        };
        AddIfPresent(attributes, "llm.interaction_id", summary.InteractionId);
        return attributes;
    }

    private static void AddIfPresent(List<KeyValuePair<string, object?>> attributes, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            attributes.Add(new(key, Truncate(value)));
        }
    }

    private static string Truncate(string value) => value.Length <= MaxPropertyLength ? value : value[..MaxPropertyLength];

    private void Write(string template, string message, List<KeyValuePair<string, object?>> attributes)
    {
        attributes.Add(new("{OriginalFormat}", template));
        _logger.Log(LogLevel.Information, default, (IReadOnlyList<KeyValuePair<string, object?>>)attributes, null, (_, _) => message);
    }
}
