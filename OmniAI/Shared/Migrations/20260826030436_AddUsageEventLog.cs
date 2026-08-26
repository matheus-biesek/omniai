using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddUsageEventLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceEventLogId",
                table: "usage_records",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "usage_event_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RedisEntryId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NextRetryAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usage_event_logs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_usage_records_SourceEventLogId",
                table: "usage_records",
                column: "SourceEventLogId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_usage_event_logs_NextRetryAt",
                table: "usage_event_logs",
                column: "NextRetryAt");

            migrationBuilder.CreateIndex(
                name: "IX_usage_event_logs_Status",
                table: "usage_event_logs",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_usage_records_usage_event_logs_SourceEventLogId",
                table: "usage_records",
                column: "SourceEventLogId",
                principalTable: "usage_event_logs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_usage_records_usage_event_logs_SourceEventLogId",
                table: "usage_records");

            migrationBuilder.DropTable(
                name: "usage_event_logs");

            migrationBuilder.DropIndex(
                name: "IX_usage_records_SourceEventLogId",
                table: "usage_records");

            migrationBuilder.DropColumn(
                name: "SourceEventLogId",
                table: "usage_records");
        }
    }
}
