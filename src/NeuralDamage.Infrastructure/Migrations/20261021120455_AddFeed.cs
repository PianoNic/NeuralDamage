using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NeuralDamage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FeedSeenAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FeedEnabled",
                table: "Bots",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxPostsPerDay",
                table: "Bots",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.CreateTable(
                name: "Posts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorBotId = table.Column<Guid>(type: "uuid", nullable: true),
                    Content = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    StickerId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceChatId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Posts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Posts_Bots_AuthorBotId",
                        column: x => x.AuthorBotId,
                        principalTable: "Bots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Posts_Chats_SourceChatId",
                        column: x => x.SourceChatId,
                        principalTable: "Chats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Posts_Stickers_StickerId",
                        column: x => x.StickerId,
                        principalTable: "Stickers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Posts_Users_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PostComments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorBotId = table.Column<Guid>(type: "uuid", nullable: true),
                    Content = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    StickerId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PostComments_Bots_AuthorBotId",
                        column: x => x.AuthorBotId,
                        principalTable: "Bots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PostComments_PostComments_ParentId",
                        column: x => x.ParentId,
                        principalTable: "PostComments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PostComments_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PostComments_Stickers_StickerId",
                        column: x => x.StickerId,
                        principalTable: "Stickers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PostComments_Users_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FeedLikes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommentId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    BotId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedLikes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeedLikes_Bots_BotId",
                        column: x => x.BotId,
                        principalTable: "Bots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FeedLikes_PostComments_CommentId",
                        column: x => x.CommentId,
                        principalTable: "PostComments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FeedLikes_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FeedLikes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FeedLikes_BotId",
                table: "FeedLikes",
                column: "BotId");

            migrationBuilder.CreateIndex(
                name: "IX_FeedLikes_CommentId",
                table: "FeedLikes",
                column: "CommentId");

            migrationBuilder.CreateIndex(
                name: "IX_FeedLikes_PostId_CommentId_BotId",
                table: "FeedLikes",
                columns: new[] { "PostId", "CommentId", "BotId" },
                unique: true,
                filter: "\"BotId\" IS NOT NULL")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_FeedLikes_PostId_CommentId_UserId",
                table: "FeedLikes",
                columns: new[] { "PostId", "CommentId", "UserId" },
                unique: true,
                filter: "\"UserId\" IS NOT NULL")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_FeedLikes_UserId",
                table: "FeedLikes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PostComments_AuthorBotId_CreatedAt",
                table: "PostComments",
                columns: new[] { "AuthorBotId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PostComments_AuthorUserId",
                table: "PostComments",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PostComments_ParentId",
                table: "PostComments",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_PostComments_PostId_CreatedAt",
                table: "PostComments",
                columns: new[] { "PostId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PostComments_StickerId",
                table: "PostComments",
                column: "StickerId");

            migrationBuilder.CreateIndex(
                name: "IX_Posts_AuthorBotId_CreatedAt",
                table: "Posts",
                columns: new[] { "AuthorBotId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Posts_AuthorUserId",
                table: "Posts",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Posts_CreatedAt",
                table: "Posts",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Posts_SourceChatId",
                table: "Posts",
                column: "SourceChatId");

            migrationBuilder.CreateIndex(
                name: "IX_Posts_StickerId",
                table: "Posts",
                column: "StickerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FeedLikes");

            migrationBuilder.DropTable(
                name: "PostComments");

            migrationBuilder.DropTable(
                name: "Posts");

            migrationBuilder.DropColumn(
                name: "FeedSeenAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "FeedEnabled",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "MaxPostsPerDay",
                table: "Bots");
        }
    }
}
