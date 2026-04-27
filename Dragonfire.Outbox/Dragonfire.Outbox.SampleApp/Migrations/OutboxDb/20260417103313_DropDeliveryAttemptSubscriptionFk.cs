using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dragonfire.Outbox.SampleApp.Migrations.OutboxDb
{
    /// <inheritdoc />
    public partial class DropDeliveryAttemptSubscriptionFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryAttempts_WebhookSubscriptions_WebhookSubscriptionId",
                schema: "outbox",
                table: "DeliveryAttempts");

            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_Lock_Candidate",
                schema: "outbox",
                table: "OutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryAttempts_MessageId_SubscriptionId",
                schema: "outbox",
                table: "DeliveryAttempts");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Lock_Candidate",
                schema: "outbox",
                table: "OutboxMessages",
                columns: new[] { "Status", "CreatedAt", "LockedUntil", "NextRetryAt" },
                filter: "[Status] IN (0, 1)")
                .Annotation("SqlServer:Include", new[] { "TenantId", "UserId", "EntityId", "EventType", "RetryCount" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryAttempts_MessageId_SubscriptionId",
                schema: "outbox",
                table: "DeliveryAttempts",
                columns: new[] { "OutboxMessageId", "WebhookSubscriptionId" })
                .Annotation("SqlServer:Include", new[] { "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_Lock_Candidate",
                schema: "outbox",
                table: "OutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryAttempts_MessageId_SubscriptionId",
                schema: "outbox",
                table: "DeliveryAttempts");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Lock_Candidate",
                schema: "outbox",
                table: "OutboxMessages",
                columns: new[] { "Status", "CreatedAt", "LockedUntil", "NextRetryAt" },
                filter: "[Status] IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryAttempts_MessageId_SubscriptionId",
                schema: "outbox",
                table: "DeliveryAttempts",
                columns: new[] { "OutboxMessageId", "WebhookSubscriptionId" });

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryAttempts_WebhookSubscriptions_WebhookSubscriptionId",
                schema: "outbox",
                table: "DeliveryAttempts",
                column: "WebhookSubscriptionId",
                principalSchema: "outbox",
                principalTable: "WebhookSubscriptions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
