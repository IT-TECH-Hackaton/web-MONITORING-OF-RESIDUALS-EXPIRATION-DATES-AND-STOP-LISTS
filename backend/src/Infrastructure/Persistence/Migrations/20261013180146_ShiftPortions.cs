using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Monitoring.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ShiftPortions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "reason",
                table: "stock_movements",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "table_number",
                table: "orders",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "shift_dishes",
                columns: table => new
                {
                    dish_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prep_portions = table.Column<int>(type: "integer", nullable: true),
                    is_stopped = table.Column<bool>(type: "boolean", nullable: false),
                    stop_reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    stop_note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    stopped_by = table.Column<Guid>(type: "uuid", nullable: true),
                    stopped_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_priority = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shift_dishes", x => x.dish_id);
                    table.ForeignKey(
                        name: "fk_shift_dishes_dishes_dish_id",
                        column: x => x.dish_id,
                        principalTable: "dishes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shift_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dish_id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    actor_login = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    text = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shift_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_shift_history_dishes_dish_id",
                        column: x => x.dish_id,
                        principalTable: "dishes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shift_history_dish_id",
                table: "shift_history",
                column: "dish_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shift_dishes");

            migrationBuilder.DropTable(
                name: "shift_history");

            migrationBuilder.DropColumn(
                name: "reason",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "table_number",
                table: "orders");
        }
    }
}
