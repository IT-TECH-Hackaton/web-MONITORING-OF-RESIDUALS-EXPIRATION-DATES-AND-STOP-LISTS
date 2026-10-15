using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Monitoring.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PartialAcceptance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "stage",
                table: "deliveries",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Accepted");

            migrationBuilder.Sql("UPDATE deliveries SET stage = 'Parked' WHERE accepted_at IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "stage",
                table: "deliveries",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldDefaultValue: "Accepted");

            migrationBuilder.AddColumn<decimal>(
                name: "accepted_quantity",
                table: "batches",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_batches_accepted_quantity",
                table: "batches",
                sql: "accepted_quantity IS NULL OR (accepted_quantity >= 0 AND accepted_quantity <= quantity_initial)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_batches_accepted_quantity",
                table: "batches");

            migrationBuilder.DropColumn(
                name: "stage",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "accepted_quantity",
                table: "batches");
        }
    }
}
