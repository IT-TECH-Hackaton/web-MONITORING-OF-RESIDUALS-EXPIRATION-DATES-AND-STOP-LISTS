using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Monitoring.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SystemChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_staff_messages_users_author_id",
                table: "staff_messages");

            migrationBuilder.DropIndex(
                name: "ix_staff_messages_created_at",
                table: "staff_messages");

            migrationBuilder.AlterColumn<Guid>(
                name: "author_id",
                table: "staff_messages",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "channel",
                table: "staff_messages",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Staff");

            migrationBuilder.CreateIndex(
                name: "ix_staff_messages_channel_created_at",
                table: "staff_messages",
                columns: new[] { "channel", "created_at" });

            migrationBuilder.AddForeignKey(
                name: "fk_staff_messages_users_author_id",
                table: "staff_messages",
                column: "author_id",
                principalTable: "users",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_staff_messages_users_author_id",
                table: "staff_messages");

            migrationBuilder.DropIndex(
                name: "ix_staff_messages_channel_created_at",
                table: "staff_messages");

            migrationBuilder.DropColumn(
                name: "channel",
                table: "staff_messages");

            migrationBuilder.AlterColumn<Guid>(
                name: "author_id",
                table: "staff_messages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_staff_messages_created_at",
                table: "staff_messages",
                column: "created_at");

            migrationBuilder.AddForeignKey(
                name: "fk_staff_messages_users_author_id",
                table: "staff_messages",
                column: "author_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
