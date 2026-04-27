namespace OutboxNet.Models;

public record DeliveryResult(
    bool Success,
    int? HttpStatusCode,
    string? ResponseBody,
    string? ErrorMessage,
    long DurationMs);
