using InboxNet.Models;

namespace InboxNet.Interfaces;

/// <summary>
/// Validates webhook authenticity. Separated from <see cref="IWebhookPayloadMapper"/> so
/// signature checks can be tested in isolation and short-circuit the pipeline without
/// any parsing side effects.
/// <para>
/// Register per-provider via keyed DI — for example
/// <c>AddKeyedScoped&lt;IWebhookSignatureValidator, MyValidator&gt;("my-provider")</c> —
/// or use the <c>AddProvider&lt;TValidator, TMapper&gt;</c> builder helper, which wires
/// both seams and exposes them as an <see cref="IWebhookProvider"/>.
/// </para>
/// </summary>
public interface IWebhookSignatureValidator
{
    Task<WebhookValidationResult> ValidateAsync(
        WebhookRequestContext context,
        CancellationToken ct = default);
}
