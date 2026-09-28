using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Finance.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "data_update_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    run_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    success = table.Column<bool>(type: "boolean", nullable: false),
                    months_written = table.Column<int>(type: "integer", nullable: false),
                    last_month = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    message = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_update_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "market_months",
                columns: table => new
                {
                    month = table.Column<DateOnly>(type: "date", nullable: false),
                    equity = table.Column<double>(type: "double precision", nullable: false),
                    bond = table.Column<double>(type: "double precision", nullable: false),
                    cash = table.Column<double>(type: "double precision", nullable: false),
                    inflation = table.Column<double>(type: "double precision", nullable: false),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_market_months", x => x.month);
                });

            migrationBuilder.CreateTable(
                name: "scenarios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    input_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scenarios", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_data_update_logs_run_at",
                table: "data_update_logs",
                column: "run_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_update_logs");

            migrationBuilder.DropTable(
                name: "market_months");

            migrationBuilder.DropTable(
                name: "scenarios");
        }
    }
}
