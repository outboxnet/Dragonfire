using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OutboxNet.SampleApp.Migrations.OutboxDb
{
    /// <inheritdoc />
    public partial class AddStatusLockedUntilIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Status_LockedUntil",
                schema: "outbox",
                table: "OutboxMessages",
                columns: new[] { "Status", "LockedUntil" },
                filter: "[LockedUntil] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_Status_LockedUntil",
                schema: "outbox",
                table: "OutboxMessages");
        }
    }
}
