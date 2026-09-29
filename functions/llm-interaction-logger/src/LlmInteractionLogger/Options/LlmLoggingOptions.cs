namespace LlmInteractionLogger.Options;

public sealed class LlmLoggingOptions
{
    public const string SectionName = "LlmLogging";

    /// <summary>Blob endpoint used with Microsoft Entra ID (managed identity), e.g. https://account.blob.core.windows.net.</summary>
    public string? BlobServiceUri { get; set; }

    /// <summary>Connection string for local development (for example Azurite). Takes precedence over <see cref="BlobServiceUri"/>.</summary>
    public string? BlobConnectionString { get; set; }

    /// <summary>Client ID of a user-assigned managed identity. Optional.</summary>
    public string? ManagedIdentityClientId { get; set; }

    public string AttachmentsContainer { get; set; } = "llm-attachments";

    public string InteractionsContainer { get; set; } = "llm-interactions";

    /// <summary>When true, the sanitized interaction JSON (binary replaced by references) is also archived to blob storage.</summary>
    public bool ArchiveInteractionJson { get; set; }

    public long MaxJsonBytes { get; set; } = 100L * 1024 * 1024;

    public long MaxAttachmentBytes { get; set; } = 100L * 1024 * 1024;

    public int MaxAttachments { get; set; } = 100;

    /// <summary>Minimum length of a bare base64 string (in a binary-looking field) before it is offloaded to blob storage. Data URIs are always offloaded.</summary>
    public int MinInlineBinaryChars { get; set; } = 256;

    /// <summary>Characters per prompt/response trace record. Application Insights truncates custom property values above 8192.</summary>
    public int ContentChunkSize { get; set; } = 8000;
}
