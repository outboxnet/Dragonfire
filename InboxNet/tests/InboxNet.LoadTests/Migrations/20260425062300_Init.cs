using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InboxNet.LoadTests.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inbox");

            migrationBuilder.CreateTable(
                name: "InboxMessages",
                schema: "inbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ProviderKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProviderEventId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    DedupKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RetryCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    LockedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    LockedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NextRetryAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    TraceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Headers = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TenantId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EntityId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InboxHandlerAttempts",
                schema: "inbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    InboxMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HandlerName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    ErrorMessage = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    AttemptedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()"),
                    NextRetryAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxHandlerAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InboxHandlerAttempts_InboxMessages_InboxMessageId",
                        column: x => x.InboxMessageId,
                        principalSchema: "inbox",
                        principalTable: "InboxMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InboxHandlerAttempts_AttemptedAt",
                schema: "inbox",
                table: "InboxHandlerAttempts",
                column: "AttemptedAt");

            migrationBuilder.CreateIndex(
                name: "IX_InboxHandlerAttempts_HandlerName_AttemptedAt",
                schema: "inbox",
                table: "InboxHandlerAttempts",
                columns: new[] { "HandlerName", "AttemptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InboxHandlerAttempts_MessageId_HandlerName",
                schema: "inbox",
                table: "InboxHandlerAttempts",
                columns: new[] { "InboxMessageId", "HandlerName" })
                .Annotation("SqlServer:Include", new[] { "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_Lock_Candidate",
                schema: "inbox",
                table: "InboxMessages",
                columns: new[] { "Status", "ReceivedAt", "LockedUntil", "NextRetryAt" },
                filter: "[Status] IN (0, 1)")
                .Annotation("SqlServer:Include", new[] { "TenantId", "ProviderKey", "EntityId", "EventType", "RetryCount" });

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_PartitionKey_Status",
                schema: "inbox",
                table: "InboxMessages",
                columns: new[] { "TenantId", "ProviderKey", "EntityId", "Status", "LockedUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_ProviderKey_EventType",
                schema: "inbox",
                table: "InboxMessages",
                columns: new[] { "ProviderKey", "EventType" });

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_Status_LockedUntil",
                schema: "inbox",
                table: "InboxMessages",
                columns: new[] { "Status", "LockedUntil" },
                filter: "[LockedUntil] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_InboxMessages_ProviderKey_DedupKey",
                schema: "inbox",
                table: "InboxMessages",
                columns: new[] { "ProviderKey", "DedupKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InboxHandlerAttempts",
                schema: "inbox");

            migrationBuilder.DropTable(
                name: "InboxMessages",
                schema: "inbox");
        }
    }
}
