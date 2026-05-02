using System.IO;

namespace Dragonfire.TraceKit.AspNetCore.Internal;

/// <summary>
/// Write-only forwarding stream that mirrors writes to the original response body and
/// to a capture buffer. Reads are not supported (kestrel never reads the response body).
/// Capture failures are swallowed — the client must always receive its bytes.
/// </summary>
internal sealed class TeeStream : Stream
{
    private readonly Stream _primary;
    private readonly Stream _capture;
    private readonly long _captureLimit;
    private long _captured;

    public TeeStream(Stream primary, Stream capture, long captureLimit = long.MaxValue)
    {
        _primary = primary;
        _capture = capture;
        _captureLimit = captureLimit;
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _primary.Length;
    public override long Position
    {
        get => _primary.Position;
        set => _primary.Position = value;
    }

    public override void Flush() => _primary.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => _primary.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => _primary.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count)
    {
        _primary.Write(buffer, offset, count);
        TryCapture(buffer.AsSpan(offset, count));
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        await _primary.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
        TryCapture(buffer.AsSpan(offset, count));
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await _primary.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        TryCapture(buffer.Span);
    }

    private void TryCapture(ReadOnlySpan<byte> span)
    {
        if (_captured >= _captureLimit) return;
        try
        {
            var remaining = _captureLimit - _captured;
            var slice = span.Length <= remaining ? span : span.Slice(0, (int)remaining);
            _capture.Write(slice);
            _captured += slice.Length;
        }
        catch
        {
            // Capture is best-effort.
        }
    }
}
