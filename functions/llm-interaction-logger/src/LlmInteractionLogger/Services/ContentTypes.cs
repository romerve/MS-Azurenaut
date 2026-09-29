namespace LlmInteractionLogger.Services;

public static class ContentTypes
{
    public const string OctetStream = "application/octet-stream";

    private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/gif"] = ".gif",
        ["image/webp"] = ".webp",
        ["image/bmp"] = ".bmp",
        ["image/tiff"] = ".tiff",
        ["image/heic"] = ".heic",
        ["image/svg+xml"] = ".svg",
        ["audio/wav"] = ".wav",
        ["audio/x-wav"] = ".wav",
        ["audio/mpeg"] = ".mp3",
        ["audio/mp3"] = ".mp3",
        ["audio/ogg"] = ".ogg",
        ["audio/flac"] = ".flac",
        ["audio/aac"] = ".aac",
        ["audio/webm"] = ".weba",
        ["audio/pcm"] = ".pcm",
        ["video/mp4"] = ".mp4",
        ["video/quicktime"] = ".mov",
        ["video/webm"] = ".webm",
        ["video/x-matroska"] = ".mkv",
        ["application/pdf"] = ".pdf",
        ["application/zip"] = ".zip",
        ["application/json"] = ".json",
        ["text/plain"] = ".txt",
        ["text/csv"] = ".csv",
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = ".docx",
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = ".xlsx",
        ["application/vnd.openxmlformats-officedocument.presentationml.presentation"] = ".pptx",
    };

    private static readonly Dictionary<string, string> AudioFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        ["wav"] = "audio/wav",
        ["mp3"] = "audio/mpeg",
        ["flac"] = "audio/flac",
        ["opus"] = "audio/ogg",
        ["ogg"] = "audio/ogg",
        ["aac"] = "audio/aac",
        ["pcm16"] = "audio/pcm",
        ["pcm"] = "audio/pcm",
    };

    public static string ExtensionFor(string contentType) =>
        Extensions.TryGetValue(Normalize(contentType), out var ext) ? ext : ".bin";

    /// <summary>Maps an OpenAI-style audio "format" value (wav, mp3, ...) to a MIME type.</summary>
    public static string? FromAudioFormat(string? format) =>
        format is not null && AudioFormats.TryGetValue(format, out var type) ? type : null;

    public static string Normalize(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return OctetStream;
        }

        var semicolon = contentType.IndexOf(';');
        var value = (semicolon >= 0 ? contentType[..semicolon] : contentType).Trim().ToLowerInvariant();
        return IsValidMediaType(value) ? value : OctetStream;
    }

    /// <summary>Resolves the most specific content type: the declared one unless it is missing/generic, then magic-byte sniffing.</summary>
    public static string Resolve(string? declared, ReadOnlySpan<byte> header)
    {
        var normalized = Normalize(declared);
        if (normalized != OctetStream)
        {
            return normalized;
        }

        return Sniff(header) ?? OctetStream;
    }

    public static string? Sniff(ReadOnlySpan<byte> b)
    {
        if (b.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return "image/png";
        if (b.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF])) return "image/jpeg";
        if (b.StartsWith("GIF87a"u8) || b.StartsWith("GIF89a"u8)) return "image/gif";
        if (b.StartsWith("%PDF-"u8)) return "application/pdf";
        if (b.Length >= 12 && b.StartsWith("RIFF"u8))
        {
            if (b[8..12].SequenceEqual("WEBP"u8)) return "image/webp";
            if (b[8..12].SequenceEqual("WAVE"u8)) return "audio/wav";
            if (b[8..12].SequenceEqual("AVI "u8)) return "video/x-msvideo";
        }
        if (b.Length >= 12 && b[4..8].SequenceEqual("ftyp"u8))
        {
            var brand = b[8..12];
            if (brand.SequenceEqual("qt  "u8)) return "video/quicktime";
            if (brand.SequenceEqual("M4A "u8)) return "audio/mp4";
            if (brand.StartsWith("hei"u8) || brand.StartsWith("mif1"u8)) return "image/heic";
            return "video/mp4";
        }
        if (b.StartsWith((ReadOnlySpan<byte>)[0x1A, 0x45, 0xDF, 0xA3])) return "video/webm";
        if (b.StartsWith("OggS"u8)) return "audio/ogg";
        if (b.StartsWith("fLaC"u8)) return "audio/flac";
        if (b.StartsWith("ID3"u8) || (b.Length >= 2 && b[0] == 0xFF && (b[1] & 0xE0) == 0xE0)) return "audio/mpeg";
        if (b.StartsWith("BM"u8) && b.Length >= 14) return "image/bmp";
        if (b.StartsWith((ReadOnlySpan<byte>)[0x50, 0x4B, 0x03, 0x04])) return "application/zip";
        return null;
    }

    private static bool IsValidMediaType(string value)
    {
        var slash = value.IndexOf('/');
        if (slash <= 0 || slash == value.Length - 1 || value.Length > 255)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '/' or '.' or '+' or '-' or '_'))
            {
                return false;
            }
        }

        return true;
    }
}
