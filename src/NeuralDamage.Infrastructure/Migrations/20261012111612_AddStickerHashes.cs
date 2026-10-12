using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NeuralDamage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStickerHashes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Sha256",
                table: "Stickers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stickers_CreatedAt",
                table: "Stickers",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Stickers_Sha256",
                table: "Stickers",
                column: "Sha256");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Stickers_CreatedAt",
                table: "Stickers");

            migrationBuilder.DropIndex(
                name: "IX_Stickers_Sha256",
                table: "Stickers");

            migrationBuilder.DropColumn(
                name: "Sha256",
                table: "Stickers");
        }
    }
}
