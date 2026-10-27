using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NeuralDamage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBotDirectMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowBotDirectMessages",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowBotDirectMessages",
                table: "Users");
        }
    }
}
