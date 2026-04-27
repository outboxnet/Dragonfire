using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using InboxNet.Interfaces;
using InboxNet.Models;

namespace InboxNet.SampleApp.Providers.Acme;

/// <summary>
/// Demo signature validator for a fictional "Acme" exchange. Expects
/// <c>X-Acme-Signature: sha256=HEX</c>, HMAC-SHA256 of the raw body with a shared secret.
/// Demonstrates the <see cref="IWebhookSignatureValidator"/> seam — signature verification
/// lives here, payload interpretation lives in <see cref="AcmePayloadMapper"/>.
/// </summary>
public sealed class AcmeSignatureValidator : IWebhookSignatureValidator
{
    private const string SignatureHeader = "X-Acme-Signature";
    private const string Prefix = "sha256=";
    private readonly byte[] _signingKey;

    public AcmeSignatureValidator(IConfiguration configuration)
    {
        var secret = configuration["Webhooks:Acme:Secret"] ?? "replace_me";
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
        using var hmac = new HMACSHA256(_signingKey);
        var expected = hmac.ComputeHash(Encoding.UTF8.GetBytes(context.RawBody));
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
            var ca = a[i];
            var cb = b[i];
            if (ca >= 'A' && ca <= 'Z') ca = (char)(ca + 32);
            if (cb >= 'A' && cb <= 'Z') cb = (char)(cb + 32);
            diff |= ca ^ cb;
        }
        return diff == 0;
    }
}
