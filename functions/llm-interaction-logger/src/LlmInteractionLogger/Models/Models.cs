using System.Text.Json.Serialization;

namespace LlmInteractionLogger.Models;

public enum AttachmentSource
{
    InlineJson,
    Multipart,
}

/// <summary>A binary payload that was offloaded to blob storage.</summary>
public sealed record StoredAttachment
{
    public required string Id { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<AttachmentSource>))]
    public required AttachmentSource Source { get; init; }

    /// <summary>JSON path of the value that was replaced (inline) or the multipart field name.</summary>
    public required string Location { get; init; }

    public string? FileName { get; init; }

    public required string ContentType { get; init; }

    public required long SizeBytes { get; init; }

    public required string Sha256 { get; init; }

    public required string Container { get; init; }

    public required string BlobName { get; init; }

    public required Uri BlobUri { get; init; }

    /// <summary>Placeholder written into the interaction JSON in place of the binary value.</summary>
    public string Reference => AttachmentReference.For(Id);
}

public static class AttachmentReference
{
    public const string Scheme = "llm-attachment://";

    public static string For(string attachmentId) => Scheme + attachmentId;
}

public sealed record StoredBlob(Uri Uri, long SizeBytes, string Sha256);

public sealed record TokenUsage(long? InputTokens, long? OutputTokens, long? TotalTokens);

/// <summary>Normalized view of an interaction used for telemetry.</summary>
public sealed record InteractionSummary
{
    public required string CaptureId { get; init; }
    public string? InteractionId { get; init; }
    public string? ConversationId { get; init; }
    public string? UserId { get; init; }
    public string? Application { get; init; }
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public string? Operation { get; init; }
    public string? Status { get; init; }
    public string? FinishReason { get; init; }
    public double? LatencyMs { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public required TokenUsage Usage { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record CaptureResult
{
    public required string CaptureId { get; init; }
    public string? InteractionId { get; init; }
    public required IReadOnlyList<StoredAttachment> Attachments { get; init; }
    /// <summary>Number of Application Insights trace records written per content kind (prompt, response, attachments).</summary>
    public required IReadOnlyDictionary<string, int> ContentChunks { get; init; }
    public Uri? ArchivedInteractionUri { get; init; }
}
