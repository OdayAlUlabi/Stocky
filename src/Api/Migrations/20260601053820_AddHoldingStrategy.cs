using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stocky.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddHoldingStrategy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Strategy",
                table: "Holdings",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "General");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Strategy",
                table: "Holdings");
        }
    }
}
