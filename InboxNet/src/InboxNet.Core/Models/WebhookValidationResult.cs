namespace InboxNet.Models;

/// <summary>
/// Output of <see cref="Interfaces.IWebhookSignatureValidator.ValidateAsync"/>.
/// Either the check passed, or the request should be rejected with
/// <see cref="FailureReason"/> surfaced as the 4xx explanation.
/// </summary>
public sealed record WebhookValidationResult
{
    public bool IsValid { get; init; }
    public string? FailureReason { get; init; }

    public static WebhookValidationResult Valid() => new() { IsValid = true };

    public static WebhookValidationResult Invalid(string reason) =>
        new() { IsValid = false, FailureReason = reason };
}
