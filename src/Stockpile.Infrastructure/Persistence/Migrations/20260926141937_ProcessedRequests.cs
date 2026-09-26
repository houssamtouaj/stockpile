using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stockpile.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProcessedRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "processed_requests",
                columns: table => new
                {
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processed_requests_idempotency_key", x => x.idempotency_key);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "processed_requests");
        }
    }
}
