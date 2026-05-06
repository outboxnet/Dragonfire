using System.Security.Cryptography;
using System.Text;
using Dragonfire.Inbox.Interfaces;
using Dragonfire.Inbox.Models;

namespace Dragonfire.WebhookPlatform.SampleApp.Inbox.LoopbackProvider;

/// <summary>
/// Validates webhooks the application has sent to itself via the outbox. Verifies the
/// <c>X-Outbox-Signature</c> header (<c>sha256=&lt;hex&gt;</c>, HMAC-SHA256 over the raw body)
/// using the shared secret configured under <c>LoopbackProvider:Secret</c>.
/// </summary>
public sealed class LoopbackSignatureValidator : IWebhookSignatureValidator
{
    private const string SignatureHeader = "X-Outbox-Signature";
    private const string Prefix = "sha256=";
    private readonly byte[] _signingKey;

    public LoopbackSignatureValidator(IConfiguration configuration)
    {
        var secret = configuration["LoopbackProvider:Secret"]
            ?? throw new InvalidOperationException("LoopbackProvider:Secret is not configured.");
        _signingKey = Encoding.UTF8.GetBytes(secret);
    }

    public Task<WebhookValidationResult> ValidateAsync(
        WebhookRequestContext context, CancellationToken ct = default)
    {
        if (!context.Headers.TryGetValue(SignatureHeader, out var header) || string.IsNullOrEmpty(header))
            return Task.FromResult(WebhookValidationResult.Invalid($"Missing {SignatureHeader}"));

        if (!header.StartsWith(Prefix, StringComparison.Ordinal))
            return Task.FromResult(WebhookValidationResult.Invalid($"{SignatureHeader} must start with '{Prefix}'"));

        var actualHex = header[Prefix.Length..];
        var expected = HMACSHA256.HashData(_signingKey, Encoding.UTF8.GetBytes(context.RawBody));
        var expectedHex = Convert.ToHexString(expected);

        return Task.FromResult(FixedTimeHexEquals(actualHex, expectedHex)
            ? WebhookValidationResult.Valid()
            : WebhookValidationResult.Invalid("Signature mismatch"));
    }

    private static bool FixedTimeHexEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++)
        {
            var ca = a[i]; var cb = b[i];
            if (ca >= 'A' && ca <= 'Z') ca = (char)(ca + 32);
            if (cb >= 'A' && cb <= 'Z') cb = (char)(cb + 32);
            diff |= ca ^ cb;
        }
        return diff == 0;
    }
}
