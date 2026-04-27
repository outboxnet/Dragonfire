using System.Text.Json.Serialization;
using InboxNet.Extensions;
using InboxNet.Interfaces;
using InboxNet.Models;

namespace InboxNet.SampleApp.Handlers;

/// <summary>
/// Targeted handler for Stripe <c>invoice.paid</c>. Registered with
/// <c>ForProvider("stripe").ForEvent("invoice.paid")</c>, so the dispatcher only invokes it
/// for matching messages. Uses <see cref="InboxMessageExtensions.PayloadAs{T}"/> to
/// deserialize into the handler-owned DTO.
/// </summary>
public sealed class StripeInvoicePaidHandler : IInboxHandler
{
    private readonly ILogger<StripeInvoicePaidHandler> _logger;

    public StripeInvoicePaidHandler(ILogger<StripeInvoicePaidHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(InboxMessage message, CancellationToken ct)
    {
        var evt = message.PayloadAs<StripeEvent>();
        var invoice = evt?.Data?.Object;

        _logger.LogInformation(
            "Stripe invoice.paid — invoice={InvoiceId} amount_paid={AmountPaid}",
            invoice?.Id, invoice?.AmountPaid ?? 0L);
        return Task.CompletedTask;
    }

    private sealed record StripeEvent([property: JsonPropertyName("data")] StripeData? Data);
    private sealed record StripeData([property: JsonPropertyName("object")] StripeInvoice? Object);
    private sealed record StripeInvoice(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("amount_paid")] long AmountPaid);
}
