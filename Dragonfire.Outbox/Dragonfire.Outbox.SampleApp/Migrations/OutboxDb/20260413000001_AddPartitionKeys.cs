using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dragonfire.Outbox.SampleApp.Migrations.OutboxDb
{
    /// <inheritdoc />
    public partial class AddPartitionKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                schema: "outbox",
                table: "OutboxMessages",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserId",
                schema: "outbox",
                table: "OutboxMessages",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EntityId",
                schema: "outbox",
                table: "OutboxMessages",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_PartitionKey",
                schema: "outbox",
                table: "OutboxMessages",
                columns: new[] { "TenantId", "UserId", "EntityId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_PartitionKey",
                schema: "outbox",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "TenantId",
                schema: "outbox",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "UserId",
                schema: "outbox",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "EntityId",
                schema: "outbox",
                table: "OutboxMessages");
        }
    }
}
