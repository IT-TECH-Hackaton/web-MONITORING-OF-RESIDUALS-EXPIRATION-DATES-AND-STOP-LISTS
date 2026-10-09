using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Monitoring.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeliveryCertificates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "supplier",
                table: "deliveries",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "certificate_expires_at",
                table: "batches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "certificate_issued_at",
                table: "batches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "certificate_issuer",
                table: "batches",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "certificate_number",
                table: "batches",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "supplier",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "certificate_expires_at",
                table: "batches");

            migrationBuilder.DropColumn(
                name: "certificate_issued_at",
                table: "batches");

            migrationBuilder.DropColumn(
                name: "certificate_issuer",
                table: "batches");

            migrationBuilder.DropColumn(
                name: "certificate_number",
                table: "batches");
        }
    }
}
