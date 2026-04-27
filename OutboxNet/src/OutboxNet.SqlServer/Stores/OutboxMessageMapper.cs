using System.Text.Json;
using Microsoft.Data.SqlClient;
using OutboxNet.Models;

namespace OutboxNet.SqlServer.Stores;

internal static class OutboxMessageMapper
{
    public static OutboxMessage MapFromReader(SqlDataReader reader)
    {
        var headersJson = reader.IsDBNull(reader.GetOrdinal("Headers"))
            ? null
            : reader.GetString(reader.GetOrdinal("Headers"));

        return new OutboxMessage
        {
            Id = reader.GetGuid(reader.GetOrdinal("Id")),
            EventType = reader.GetString(reader.GetOrdinal("EventType")),
            Payload = reader.GetString(reader.GetOrdinal("Payload")),
            CorrelationId = reader.IsDBNull(reader.GetOrdinal("CorrelationId")) ? null : reader.GetString(reader.GetOrdinal("CorrelationId")),
            TraceId = reader.IsDBNull(reader.GetOrdinal("TraceId")) ? null : reader.GetString(reader.GetOrdinal("TraceId")),
            Status = (MessageStatus)reader.GetInt32(reader.GetOrdinal("Status")),
            RetryCount = reader.GetInt32(reader.GetOrdinal("RetryCount")),
            CreatedAt = reader.GetDateTimeOffset(reader.GetOrdinal("CreatedAt")),
            ProcessedAt = reader.IsDBNull(reader.GetOrdinal("ProcessedAt")) ? null : reader.GetDateTimeOffset(reader.GetOrdinal("ProcessedAt")),
            LockedUntil = reader.IsDBNull(reader.GetOrdinal("LockedUntil")) ? null : reader.GetDateTimeOffset(reader.GetOrdinal("LockedUntil")),
            LockedBy = reader.IsDBNull(reader.GetOrdinal("LockedBy")) ? null : reader.GetString(reader.GetOrdinal("LockedBy")),
            NextRetryAt = reader.IsDBNull(reader.GetOrdinal("NextRetryAt")) ? null : reader.GetDateTimeOffset(reader.GetOrdinal("NextRetryAt")),
            LastError = reader.IsDBNull(reader.GetOrdinal("LastError")) ? null : reader.GetString(reader.GetOrdinal("LastError")),
            Headers = headersJson == null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson),
            TenantId = reader.IsDBNull(reader.GetOrdinal("TenantId")) ? null : reader.GetString(reader.GetOrdinal("TenantId")),
            UserId = reader.IsDBNull(reader.GetOrdinal("UserId")) ? null : reader.GetString(reader.GetOrdinal("UserId")),
            EntityId = reader.IsDBNull(reader.GetOrdinal("EntityId")) ? null : reader.GetString(reader.GetOrdinal("EntityId"))
        };
    }

    public static WebhookSubscription MapSubscriptionFromReader(SqlDataReader reader)
    {
        var customHeadersJson = reader.IsDBNull(reader.GetOrdinal("CustomHeaders"))
            ? null
            : reader.GetString(reader.GetOrdinal("CustomHeaders"));

        return new WebhookSubscription
        {
            Id = reader.GetGuid(reader.GetOrdinal("Id")),
            TenantId = reader.IsDBNull(reader.GetOrdinal("TenantId")) ? null : reader.GetString(reader.GetOrdinal("TenantId")),
            EventType = reader.GetString(reader.GetOrdinal("EventType")),
            WebhookUrl = reader.GetString(reader.GetOrdinal("WebhookUrl")),
            Secret = reader.GetString(reader.GetOrdinal("Secret")),
            IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
            MaxRetries = reader.GetInt32(reader.GetOrdinal("MaxRetries")),
            Timeout = TimeSpan.FromSeconds(reader.GetInt32(reader.GetOrdinal("TimeoutSeconds"))),
            CreatedAt = reader.GetDateTimeOffset(reader.GetOrdinal("CreatedAt")),
            UpdatedAt = reader.IsDBNull(reader.GetOrdinal("UpdatedAt")) ? null : reader.GetDateTimeOffset(reader.GetOrdinal("UpdatedAt")),
            CustomHeaders = customHeadersJson == null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(customHeadersJson)
        };
    }

    public static DeliveryAttempt MapDeliveryAttemptFromReader(SqlDataReader reader)
    {
        return new DeliveryAttempt
        {
            Id = reader.GetGuid(reader.GetOrdinal("Id")),
            OutboxMessageId = reader.GetGuid(reader.GetOrdinal("OutboxMessageId")),
            WebhookSubscriptionId = reader.GetGuid(reader.GetOrdinal("WebhookSubscriptionId")),
            AttemptNumber = reader.GetInt32(reader.GetOrdinal("AttemptNumber")),
            Status = (DeliveryStatus)reader.GetInt32(reader.GetOrdinal("Status")),
            HttpStatusCode = reader.IsDBNull(reader.GetOrdinal("HttpStatusCode")) ? null : reader.GetInt32(reader.GetOrdinal("HttpStatusCode")),
            ResponseBody = reader.IsDBNull(reader.GetOrdinal("ResponseBody")) ? null : reader.GetString(reader.GetOrdinal("ResponseBody")),
            ErrorMessage = reader.IsDBNull(reader.GetOrdinal("ErrorMessage")) ? null : reader.GetString(reader.GetOrdinal("ErrorMessage")),
            DurationMs = reader.GetInt64(reader.GetOrdinal("DurationMs")),
            AttemptedAt = reader.GetDateTimeOffset(reader.GetOrdinal("AttemptedAt")),
            NextRetryAt = reader.IsDBNull(reader.GetOrdinal("NextRetryAt")) ? null : reader.GetDateTimeOffset(reader.GetOrdinal("NextRetryAt"))
        };
    }
}
