using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NeuralDamage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWebImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceUrl",
                table: "GeneratedImages",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SourceUrl",
                table: "GeneratedImages");
        }
    }
}
