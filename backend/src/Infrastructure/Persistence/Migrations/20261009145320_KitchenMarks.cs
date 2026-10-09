using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Monitoring.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class KitchenMarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "kitchen_note",
                table: "order_items",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kitchen_status",
                table: "order_items",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Queued");

            migrationBuilder.AddColumn<string>(
                name: "kitchen_note",
                table: "batches",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "opened_at",
                table: "batches",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "kitchen_note",
                table: "order_items");

            migrationBuilder.DropColumn(
                name: "kitchen_status",
                table: "order_items");

            migrationBuilder.DropColumn(
                name: "kitchen_note",
                table: "batches");

            migrationBuilder.DropColumn(
                name: "opened_at",
                table: "batches");
        }
    }
}
