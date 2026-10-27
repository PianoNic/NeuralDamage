using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NeuralDamage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMusic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MusicStart",
                table: "Stories",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SongId",
                table: "Stories",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MusicStart",
                table: "Posts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SongId",
                table: "Posts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Songs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeezerId = table.Column<long>(type: "bigint", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Artist = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Album = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CoverUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    HasPreview = table.Column<bool>(type: "boolean", nullable: false),
                    FullContentType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FullSeconds = table.Column<int>(type: "integer", nullable: false),
                    FullFailedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Songs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Stories_SongId",
                table: "Stories",
                column: "SongId");

            migrationBuilder.CreateIndex(
                name: "IX_Posts_SongId",
                table: "Posts",
                column: "SongId");

            migrationBuilder.CreateIndex(
                name: "IX_Songs_DeezerId",
                table: "Songs",
                column: "DeezerId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Posts_Songs_SongId",
                table: "Posts",
                column: "SongId",
                principalTable: "Songs",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Stories_Songs_SongId",
                table: "Stories",
                column: "SongId",
                principalTable: "Songs",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Posts_Songs_SongId",
                table: "Posts");

            migrationBuilder.DropForeignKey(
                name: "FK_Stories_Songs_SongId",
                table: "Stories");

            migrationBuilder.DropTable(
                name: "Songs");

            migrationBuilder.DropIndex(
                name: "IX_Stories_SongId",
                table: "Stories");

            migrationBuilder.DropIndex(
                name: "IX_Posts_SongId",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "MusicStart",
                table: "Stories");

            migrationBuilder.DropColumn(
                name: "SongId",
                table: "Stories");

            migrationBuilder.DropColumn(
                name: "MusicStart",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "SongId",
                table: "Posts");
        }
    }
}
