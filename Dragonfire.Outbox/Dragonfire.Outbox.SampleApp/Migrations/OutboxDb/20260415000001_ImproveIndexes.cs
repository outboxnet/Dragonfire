using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dragonfire.Outbox.SampleApp.Migrations.OutboxDb
{
    /// <inheritdoc />
    public partial class ImproveIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── OutboxMessages ─────────────────────────────────────────────────

            // Drop old partial/fragmented indexes that are superseded.
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_Status_NextRetryAt",
                schema: "outbox",
                table: "OutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_CreatedAt",
                schema: "outbox",
                table: "OutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_PartitionKey",
                schema: "outbox",
                table: "OutboxMessages");

            // Primary candidate-scan index for LockNextBatchAsync.
            // Filtered to active rows only; INCLUDE covers OUTPUT columns to avoid key-lookups.
            migrationBuilder.Sql("""
                CREATE INDEX [IX_OutboxMessages_Lock_Candidate]
                    ON [outbox].[OutboxMessages] ([Status], [CreatedAt], [LockedUntil], [NextRetryAt])
                    INCLUDE ([TenantId], [UserId], [EntityId], [EventType], [RetryCount])
                    WHERE [Status] IN (0, 1)
                """);

            // Ordered-processing NOT EXISTS sub-query scan (replaces old PartitionKey index).
            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_PartitionKey_Status",
                schema: "outbox",
                table: "OutboxMessages",
                columns: new[] { "TenantId", "UserId", "EntityId", "Status", "LockedUntil" });

            // ── DeliveryAttempts ───────────────────────────────────────────────

            // Drop old indexes superseded by improved ones.
            migrationBuilder.DropIndex(
                name: "IX_DeliveryAttempts_MessageId",
                schema: "outbox",
                table: "DeliveryAttempts");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryAttempts_SubscriptionId_Status",
                schema: "outbox",
                table: "DeliveryAttempts");

            // Composite index covers GetDeliveryStatesAsync GROUP BY in one seek.
            // INCLUDE Status avoids key-lookup for MAX(CASE WHEN Status=1...) aggregate.
            migrationBuilder.Sql("""
                CREATE INDEX [IX_DeliveryAttempts_MessageId_SubscriptionId]
                    ON [outbox].[DeliveryAttempts] ([OutboxMessageId], [WebhookSubscriptionId])
                    INCLUDE ([Status])
                """);

            // GetBySubscriptionIdAsync / admin queries ordered by recency.
            migrationBuilder.CreateIndex(
                name: "IX_DeliveryAttempts_SubscriptionId_AttemptedAt",
                schema: "outbox",
                table: "DeliveryAttempts",
                columns: new[] { "WebhookSubscriptionId", "AttemptedAt" });

            // PurgeOldAttemptsAsync: DELETE WHERE AttemptedAt < @olderThan
            migrationBuilder.CreateIndex(
                name: "IX_DeliveryAttempts_AttemptedAt",
                schema: "outbox",
                table: "DeliveryAttempts",
                column: "AttemptedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_Lock_Candidate",
                schema: "outbox",
                table: "OutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_PartitionKey_Status",
                schema: "outbox",
                table: "OutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryAttempts_MessageId_SubscriptionId",
                schema: "outbox",
                table: "DeliveryAttempts");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryAttempts_SubscriptionId_AttemptedAt",
                schema: "outbox",
                table: "DeliveryAttempts");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryAttempts_AttemptedAt",
                schema: "outbox",
                table: "DeliveryAttempts");

            // Restore dropped indexes.
            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Status_NextRetryAt",
                schema: "outbox",
                table: "OutboxMessages",
                columns: new[] { "Status", "NextRetryAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_CreatedAt",
                schema: "outbox",
                table: "OutboxMessages",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_PartitionKey",
                schema: "outbox",
                table: "OutboxMessages",
                columns: new[] { "TenantId", "UserId", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryAttempts_MessageId",
                schema: "outbox",
                table: "DeliveryAttempts",
                column: "OutboxMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryAttempts_SubscriptionId_Status",
                schema: "outbox",
                table: "DeliveryAttempts",
                columns: new[] { "WebhookSubscriptionId", "Status" });
        }
    }
}
