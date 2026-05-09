using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using Dragonfire.IdempotentApi.Interfaces;
using Microsoft.AspNetCore.Http;

namespace Dragonfire.IdempotentApi.Fingerprints;

/// <summary>
/// SHA-256 over <c>METHOD PATH \n BODY</c>. Method+path are mixed in so reusing the
/// same idempotency key against a different route is treated as a fingerprint mismatch.
/// </summary>
public sealed class Sha256BodyFingerprintCalculator : IRequestFingerprintCalculator
{
    public async Task<string> CalculateAsync(HttpContext context, CancellationToken ct)
    {
        // EnableBuffering allows the body stream to be re-read by the downstream handler
        // after we've consumed it for the hash.
        context.Request.EnableBuffering();

        if (context.Request.Body.CanSeek)
            context.Request.Body.Position = 0;

        using var sha = SHA256.Create();

        var prefix = Encoding.UTF8.GetBytes(
            context.Request.Method + " " + context.Request.Path.ToString() + "\n");
        sha.TransformBlock(prefix, 0, prefix.Length, null, 0);

        var buffer = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            int read;
            while ((read = await context.Request.Body.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                sha.TransformBlock(buffer, 0, read, null, 0);

            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (context.Request.Body.CanSeek)
            context.Request.Body.Position = 0;

        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }
}
