using System.Security.Cryptography;
using LlmInteractionLogger.Models;

namespace LlmInteractionLogger.Services;

/// <summary>
/// Read-only pass-through stream that computes SHA-256 and enforces a maximum length while a blob is uploaded,
/// so large multipart attachments are never buffered in memory.
/// </summary>
public sealed class HashingLimitStream(Stream inner, long maxBytes) : Stream
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private string? _sha256;

    public long BytesRead { get; private set; }

    public string Sha256 => _sha256 ??= Convert.ToHexStringLower(_hash.GetHashAndReset());

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => BytesRead; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var read = inner.Read(buffer);
        Track(buffer[..read]);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        Track(buffer.Span[..read]);
        return read;
    }

    private void Track(ReadOnlySpan<byte> data)
    {
        BytesRead += data.Length;
        if (BytesRead > maxBytes)
        {
            throw new PayloadTooLargeException($"Attachment exceeds the {maxBytes} byte limit.");
        }

        _hash.AppendData(data);
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hash.Dispose();
        }

        base.Dispose(disposing);
    }
}
