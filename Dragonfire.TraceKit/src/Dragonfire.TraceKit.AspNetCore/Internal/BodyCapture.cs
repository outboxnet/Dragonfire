using System.Buffers;
using System.IO;
using System.Text;
using Dragonfire.TraceKit.Options;

namespace Dragonfire.TraceKit.AspNetCore.Internal;

/// <summary>
/// Helpers for sniffing content types and reading bounded amounts of stream/byte data
/// without allocating more than <see cref="TraceKitOptions.MaxBodyBytes"/>.
/// </summary>
internal static class BodyCapture
{
    public static bool ShouldCapture(string? contentType, TraceKitOptions options)
    {
        if (string.IsNullOrEmpty(contentType)) return true; // unknown - try anyway, redactor handles non-JSON
        foreach (var prefix in options.CapturableContentTypePrefixes)
        {
            if (contentType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static async Task<string?> ReadStreamAsync(
        Stream stream,
        Encoding encoding,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        if (!stream.CanRead) return null;
        if (maxBytes <= 0) return null;

        var buffer = ArrayPool<byte>.Shared.Rent(maxBytes);
        try
        {
            var totalRead = 0;
            while (totalRead < maxBytes)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(totalRead, maxBytes - totalRead), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0) break;
                totalRead += read;
            }

            if (totalRead == 0) return string.Empty;

            var truncated = totalRead == maxBytes && stream.CanRead && PeekHasMore(stream);
            var text = encoding.GetString(buffer, 0, totalRead);
            return truncated ? text + $" … [truncated, captured first {maxBytes} bytes]" : text;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static string ReadBytes(byte[] data, Encoding encoding, int maxBytes)
    {
        if (data.Length <= maxBytes)
            return encoding.GetString(data);
        return encoding.GetString(data, 0, maxBytes) + $" … [truncated, captured first {maxBytes} of {data.Length} bytes]";
    }

    private static bool PeekHasMore(Stream stream)
    {
        try
        {
            return stream.CanSeek && stream.Position < stream.Length;
        }
        catch
        {
            return false;
        }
    }
}
