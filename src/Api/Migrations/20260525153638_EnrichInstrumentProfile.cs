using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stocky.Api.Migrations
{
    /// <inheritdoc />
    public partial class EnrichInstrumentProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsEasyToBorrow",
                table: "Instruments",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFractionable",
                table: "Instruments",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsMarginable",
                table: "Instruments",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsShortable",
                table: "Instruments",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsTradable",
                table: "Instruments",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaintenanceMarginRequirement",
                table: "Instruments",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProfileUpdatedAt",
                table: "Instruments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Instruments",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsEasyToBorrow",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "IsFractionable",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "IsMarginable",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "IsShortable",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "IsTradable",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "MaintenanceMarginRequirement",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "ProfileUpdatedAt",
                table: "Instruments");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Instruments");
        }
    }
}
