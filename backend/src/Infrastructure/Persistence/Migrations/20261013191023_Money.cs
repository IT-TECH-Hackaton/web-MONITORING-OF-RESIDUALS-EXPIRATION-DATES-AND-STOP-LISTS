using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Monitoring.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Money : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "display_name",
                table: "users",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "purchase_price",
                table: "products",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "sale_price",
                table: "dishes",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "received_by",
                table: "deliveries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "unit_cost",
                table: "batches",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_received_by",
                table: "deliveries",
                column: "received_by");

            migrationBuilder.AddForeignKey(
                name: "fk_deliveries_users_received_by",
                table: "deliveries",
                column: "received_by",
                principalTable: "users",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_deliveries_users_received_by",
                table: "deliveries");

            migrationBuilder.DropIndex(
                name: "ix_deliveries_received_by",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "display_name",
                table: "users");

            migrationBuilder.DropColumn(
                name: "purchase_price",
                table: "products");

            migrationBuilder.DropColumn(
                name: "sale_price",
                table: "dishes");

            migrationBuilder.DropColumn(
                name: "received_by",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "unit_cost",
                table: "batches");
        }
    }
}
