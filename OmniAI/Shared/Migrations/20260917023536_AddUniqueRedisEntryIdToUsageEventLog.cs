using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueRedisEntryIdToUsageEventLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Bancos criados antes deste indice podem ja ter a mesma entrada do Redis registrada mais
            // de uma vez (e o custo contado em dobro). Sem limpar, o CreateIndex falha e o migrator
            // impede o docker compose de subir. Mantem um log por entrada - de preferencia o que ja
            // gerou UsageRecord - e remove os demais junto com seus UsageRecords duplicados.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE duplicate_usage_event_logs AS
                SELECT "Id" FROM (
                    SELECT l."Id", ROW_NUMBER() OVER (
                        PARTITION BY l."RedisEntryId"
                        ORDER BY (r."Id" IS NULL), l."ReceivedAt", l."Id") AS position
                    FROM usage_event_logs l
                    LEFT JOIN usage_records r ON r."SourceEventLogId" = l."Id"
                ) ranked
                WHERE position > 1;

                DELETE FROM usage_records WHERE "SourceEventLogId" IN (SELECT "Id" FROM duplicate_usage_event_logs);
                DELETE FROM usage_event_logs WHERE "Id" IN (SELECT "Id" FROM duplicate_usage_event_logs);

                DROP TABLE duplicate_usage_event_logs;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_usage_event_logs_RedisEntryId",
                table: "usage_event_logs",
                column: "RedisEntryId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_usage_event_logs_RedisEntryId",
                table: "usage_event_logs");
        }
    }
}
