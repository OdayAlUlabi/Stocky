using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stocky.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddHoldingTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "StopLoss",
                table: "Holdings",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Target1",
                table: "Holdings",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Target2",
                table: "Holdings",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Target3",
                table: "Holdings",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StopLoss",
                table: "Holdings");

            migrationBuilder.DropColumn(
                name: "Target1",
                table: "Holdings");

            migrationBuilder.DropColumn(
                name: "Target2",
                table: "Holdings");

            migrationBuilder.DropColumn(
                name: "Target3",
                table: "Holdings");
        }
    }
}
