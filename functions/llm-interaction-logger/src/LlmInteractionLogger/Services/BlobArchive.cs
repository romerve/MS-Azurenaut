using System.Collections.Concurrent;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using LlmInteractionLogger.Models;

namespace LlmInteractionLogger.Services;

public interface IBlobArchive
{
    /// <summary>Streams <paramref name="content"/> to a new blob, computing its SHA-256 and enforcing <paramref name="maxBytes"/>.</summary>
    Task<StoredBlob> UploadAsync(
        string container,
        string blobName,
        Stream content,
        string contentType,
        IReadOnlyDictionary<string, string> metadata,
        long maxBytes,
        CancellationToken cancellationToken);

    Task DeleteAsync(string container, string blobName, CancellationToken cancellationToken);
}

public sealed class BlobArchive(BlobServiceClient serviceClient) : IBlobArchive
{
    private readonly ConcurrentDictionary<string, bool> _readyContainers = new();

    public async Task<StoredBlob> UploadAsync(
        string container,
        string blobName,
        Stream content,
        string contentType,
        IReadOnlyDictionary<string, string> metadata,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var containerClient = serviceClient.GetBlobContainerClient(container);
        await EnsureContainerAsync(containerClient, cancellationToken).ConfigureAwait(false);

        var blob = containerClient.GetBlobClient(blobName);
        await using var hashing = new HashingLimitStream(content, maxBytes);
        try
        {
            await blob.UploadAsync(
                hashing,
                new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                    Metadata = new Dictionary<string, string>(metadata),
                    // Never overwrite an existing capture.
                    Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (hashing.BytesRead > maxBytes)
        {
            await blob.DeleteIfExistsAsync(cancellationToken: CancellationToken.None).ConfigureAwait(false);
            throw ex as PayloadTooLargeException ?? new PayloadTooLargeException($"Attachment exceeds the {maxBytes} byte limit.");
        }

        var sha256 = hashing.Sha256;
        var finalMetadata = new Dictionary<string, string>(metadata) { ["sha256"] = sha256 };
        await blob.SetMetadataAsync(finalMetadata, cancellationToken: cancellationToken).ConfigureAwait(false);

        return new StoredBlob(blob.Uri, hashing.BytesRead, sha256);
    }

    public Task DeleteAsync(string container, string blobName, CancellationToken cancellationToken) =>
        serviceClient.GetBlobContainerClient(container).GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: cancellationToken);

    private async Task EnsureContainerAsync(BlobContainerClient container, CancellationToken cancellationToken)
    {
        if (_readyContainers.ContainsKey(container.Name))
        {
            return;
        }

        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken).ConfigureAwait(false);
        _readyContainers.TryAdd(container.Name, true);
    }
}
