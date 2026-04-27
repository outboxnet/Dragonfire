using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dragonfire.Outbox.SampleApp.Migrations.OutboxDb
{
    /// <inheritdoc />
    public partial class AddWebhookTenantId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                schema: "outbox",
                table: "WebhookSubscriptions",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebhookSubscriptions_TenantId_Active",
                schema: "outbox",
                table: "WebhookSubscriptions",
                columns: new[] { "TenantId", "IsActive" },
                filter: "[TenantId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WebhookSubscriptions_TenantId_Active",
                schema: "outbox",
                table: "WebhookSubscriptions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                schema: "outbox",
                table: "WebhookSubscriptions");
        }
    }
}
