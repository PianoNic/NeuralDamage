using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NeuralDamage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StoryReplyReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "StoryAuthorBotId",
                table: "Messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoryAuthorName",
                table: "Messages",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StoryAuthorUserId",
                table: "Messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StoryId",
                table: "Messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoryText",
                table: "Messages",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StoryAuthorBotId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "StoryAuthorName",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "StoryAuthorUserId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "StoryId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "StoryText",
                table: "Messages");
        }
    }
}
