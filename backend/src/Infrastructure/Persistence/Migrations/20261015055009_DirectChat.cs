using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Monitoring.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DirectChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "recipient_id",
                table: "staff_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_staff_messages_recipient_id",
                table: "staff_messages",
                column: "recipient_id");

            migrationBuilder.AddForeignKey(
                name: "fk_staff_messages_users_recipient_id",
                table: "staff_messages",
                column: "recipient_id",
                principalTable: "users",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_staff_messages_users_recipient_id",
                table: "staff_messages");

            migrationBuilder.DropIndex(
                name: "ix_staff_messages_recipient_id",
                table: "staff_messages");

            migrationBuilder.DropColumn(
                name: "recipient_id",
                table: "staff_messages");
        }
    }
}
