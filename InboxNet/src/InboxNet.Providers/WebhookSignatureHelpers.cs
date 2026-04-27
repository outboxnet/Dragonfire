using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace InboxNet.Providers;

internal static class WebhookSignatureHelpers
{
    // 4 KB stackalloc threshold leaves ample headroom on the default 1 MB stack while
    // covering the vast majority of webhook bodies in one shot. Larger payloads fall back
    // to a pooled buffer so we never grow the managed heap with the body twice.
    private const int StackallocThreshold = 4 * 1024;

    private static readonly char[] LowerHex = "0123456789abcdef".ToCharArray();

    /// <summary>
    /// Computes the dedup-fallback SHA only when it will actually be used: either the caller
    /// has no provider-supplied event ID, or the inbox is configured to always compute it
    /// for forensics. Returns <see cref="string.Empty"/> when skipped — the publisher's
    /// dedup key falls back to <paramref name="providerEventId"/> in that case.
    /// </summary>
    public static string ComputeContentSha256IfNeeded(string body, string? providerEventId, bool alwaysCompute)
        => alwaysCompute || string.IsNullOrEmpty(providerEventId)
            ? ComputeContentSha256(body)
            : string.Empty;

    /// <summary>
    /// Computes lowercase hex SHA-256 of a UTF-8 string. Avoids the intermediate
    /// <c>Encoding.UTF8.GetBytes</c> allocation by encoding into a stackalloc/pooled
    /// buffer, and writes the lowercase hex directly without an upper→lower copy.
    /// </summary>
    public static string ComputeContentSha256(string body)
    {
        Span<byte> hash = stackalloc byte[32];
        EncodeAndHash(body, hash, hmacKey: null);
        return ToLowerHex(hash);
    }

    /// <summary>
    /// Computes HMAC-SHA256 of <paramref name="message"/> using <paramref name="key"/>.
    /// Returns the raw signature bytes for constant-time comparison.
    /// </summary>
    public static byte[] ComputeHmacSha256(byte[] key, string message)
    {
        var hash = new byte[32];
        EncodeAndHash(message, hash, key);
        return hash;
    }

    private static void EncodeAndHash(string input, Span<byte> destination, byte[]? hmacKey)
    {
        var maxByteCount = Encoding.UTF8.GetMaxByteCount(input.Length);
        if (maxByteCount <= StackallocThreshold)
        {
            Span<byte> bytes = stackalloc byte[StackallocThreshold];
            var written = Encoding.UTF8.GetBytes(input.AsSpan(), bytes);
            if (hmacKey is null)
                SHA256.HashData(bytes[..written], destination);
            else
                HMACSHA256.HashData(hmacKey, bytes[..written], destination);
        }
        else
        {
            var rented = ArrayPool<byte>.Shared.Rent(maxByteCount);
            try
            {
                var written = Encoding.UTF8.GetBytes(input.AsSpan(), rented);
                if (hmacKey is null)
                    SHA256.HashData(rented.AsSpan(0, written), destination);
                else
                    HMACSHA256.HashData(hmacKey, rented.AsSpan(0, written), destination);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    /// <summary>
    /// Constant-time comparison of two hex-encoded signatures. Case-insensitive.
    /// Returns false if the inputs have different lengths.
    /// </summary>
    public static bool FixedTimeHexEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++)
        {
            var ca = a[i];
            var cb = b[i];
            if (ca >= 'A' && ca <= 'Z') ca = (char)(ca + 32);
            if (cb >= 'A' && cb <= 'Z') cb = (char)(cb + 32);
            diff |= ca ^ cb;
        }
        return diff == 0;
    }

    /// <summary>
    /// Constant-time comparison between a hex-encoded signature and raw bytes. Compares
    /// against an inline-computed lowercase hex of the raw bytes, without allocating an
    /// intermediate <c>Convert.ToHexString</c> string.
    /// </summary>
    public static bool FixedTimeHexEquals(string hex, byte[] raw)
    {
        if (hex.Length != raw.Length * 2) return false;
        var diff = 0;
        for (var i = 0; i < raw.Length; i++)
        {
            var hiExpected = LowerHex[raw[i] >> 4];
            var loExpected = LowerHex[raw[i] & 0xF];

            var hi = hex[i * 2];
            var lo = hex[i * 2 + 1];
            if (hi >= 'A' && hi <= 'Z') hi = (char)(hi + 32);
            if (lo >= 'A' && lo <= 'Z') lo = (char)(lo + 32);

            diff |= hi ^ hiExpected;
            diff |= lo ^ loExpected;
        }
        return diff == 0;
    }

    private static string ToLowerHex(ReadOnlySpan<byte> bytes)
    {
        Span<char> hex = stackalloc char[bytes.Length * 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            hex[i * 2] = LowerHex[bytes[i] >> 4];
            hex[i * 2 + 1] = LowerHex[bytes[i] & 0xF];
        }
        return new string(hex);
    }
}
