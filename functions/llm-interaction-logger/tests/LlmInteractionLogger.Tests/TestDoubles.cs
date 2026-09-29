using System.Collections.Concurrent;
using System.Security.Cryptography;
using LlmInteractionLogger.Models;
using LlmInteractionLogger.Services;
using LlmInteractionLogger.Telemetry;

namespace LlmInteractionLogger.Tests;

internal static class Samples
{
    // 1x1 transparent PNG.
    public static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

    public static readonly byte[] Wav = [.. "RIFF"u8.ToArray(), 0x24, 0, 0, 0, .. "WAVEfmt "u8.ToArray(), .. new byte[300]];

    public static readonly byte[] Mp4 = [0, 0, 0, 0x18, .. "ftypmp42"u8.ToArray(), .. new byte[400]];

    public static readonly byte[] Pdf = [.. "%PDF-1.7\n"u8.ToArray(), .. new byte[300]];

    public static string B64(byte[] data) => Convert.ToBase64String(data);

    public static string DataUri(string mime, byte[] data) => $"data:{mime};base64,{B64(data)}";
}

internal sealed class FakeBlobArchive : IBlobArchive
{
    public ConcurrentDictionary<string, (byte[] Data, string ContentType, IReadOnlyDictionary<string, string> Metadata)> Blobs { get; } = new();

    public List<string> Deleted { get; } = [];

    public async Task<StoredBlob> UploadAsync(
        string container,
        string blobName,
        Stream content,
        string contentType,
        IReadOnlyDictionary<string, string> metadata,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        await using var hashing = new HashingLimitStream(content, maxBytes);
        using var buffer = new MemoryStream();
        await hashing.CopyToAsync(buffer, cancellationToken);
        var key = $"{container}/{blobName}";
        if (!Blobs.TryAdd(key, (buffer.ToArray(), contentType, metadata)))
        {
            throw new InvalidOperationException($"Blob {key} already exists.");
        }

        return new StoredBlob(new Uri($"https://fake.blob.core.windows.net/{key}"), hashing.BytesRead, hashing.Sha256);
    }

    public Task DeleteAsync(string container, string blobName, CancellationToken cancellationToken)
    {
        var key = $"{container}/{blobName}";
        Deleted.Add(key);
        Blobs.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
}

internal sealed class FakeTelemetry : ILlmTelemetry
{
    public InteractionSummary? Summary { get; private set; }
    public IReadOnlyList<StoredAttachment> Attachments { get; private set; } = [];
    public IReadOnlyList<ContentPart> Contents { get; private set; } = [];
    public Uri? ArchiveUri { get; private set; }

    public IReadOnlyDictionary<string, int> Track(
        InteractionSummary summary,
        IReadOnlyList<StoredAttachment> attachments,
        IReadOnlyList<ContentPart> contents,
        Uri? archivedInteractionUri)
    {
        Summary = summary;
        Attachments = attachments.ToList();
        Contents = contents;
        ArchiveUri = archivedInteractionUri;
        return contents.ToDictionary(c => c.Kind, _ => 1);
    }

    public string Content(string kind) => Contents.Single(c => c.Kind == kind).Content;
}
