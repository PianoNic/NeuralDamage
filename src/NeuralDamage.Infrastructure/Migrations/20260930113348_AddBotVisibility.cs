using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NeuralDamage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBotVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ChatId",
                table: "Bots",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                table: "Bots",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bots_ChatId",
                table: "Bots",
                column: "ChatId");

            migrationBuilder.AddForeignKey(
                name: "FK_Bots_Chats_ChatId",
                table: "Bots",
                column: "ChatId",
                principalTable: "Chats",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bots_Chats_ChatId",
                table: "Bots");

            migrationBuilder.DropIndex(
                name: "IX_Bots_ChatId",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "ChatId",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "IsPublic",
                table: "Bots");
        }
    }
}
