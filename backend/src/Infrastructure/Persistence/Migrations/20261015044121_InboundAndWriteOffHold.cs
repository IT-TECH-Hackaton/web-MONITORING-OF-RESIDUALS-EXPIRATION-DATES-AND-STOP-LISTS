using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Monitoring.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InboundAndWriteOffHold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "quantity_before",
                table: "stock_movements",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "accepted_at",
                table: "deliveries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("UPDATE deliveries SET accepted_at = created_at WHERE accepted_at IS NULL;");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_order_id",
                table: "stock_movements",
                column: "order_id");

            migrationBuilder.AddForeignKey(
                name: "fk_stock_movements_orders_order_id",
                table: "stock_movements",
                column: "order_id",
                principalTable: "orders",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_stock_movements_orders_order_id",
                table: "stock_movements");

            migrationBuilder.DropIndex(
                name: "ix_stock_movements_order_id",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "quantity_before",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "accepted_at",
                table: "deliveries");
        }
    }
}
