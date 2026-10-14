using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Monitoring.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServiceShift : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "service_shifts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    opened_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    opened_by = table.Column<Guid>(type: "uuid", nullable: false),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_shifts", x => x.id);
                    table.ForeignKey(
                        name: "fk_service_shifts_users_closed_by",
                        column: x => x.closed_by,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_service_shifts_users_opened_by",
                        column: x => x.opened_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_service_shifts_closed_at",
                table: "service_shifts",
                column: "closed_at");

            migrationBuilder.CreateIndex(
                name: "ix_service_shifts_closed_by",
                table: "service_shifts",
                column: "closed_by");

            migrationBuilder.CreateIndex(
                name: "ix_service_shifts_opened_by",
                table: "service_shifts",
                column: "opened_by");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "service_shifts");
        }
    }
}
