using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NeuralDamage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddComposedImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "GeneratedImages",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Generated");

            migrationBuilder.AddColumn<string>(
                name: "Spec",
                table: "GeneratedImages",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kind",
                table: "GeneratedImages");

            migrationBuilder.DropColumn(
                name: "Spec",
                table: "GeneratedImages");
        }
    }
}
