using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NeuralDamage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDirectChats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DirectKey",
                table: "Chats",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            // The chats story replies made, two members and named after the other one, become the
            // direct chats; the oldest wins where a pair has more than one. Chats someone named stay groups.
            // uuids compare byte by byte, the same order as Chat.DirectKeyOf's ordinal one on their text.
            migrationBuilder.Sql("""
                WITH pairs AS (
                    SELECT c."Id",
                           LEAST(me."UserId", COALESCE(o."BotId", o."UserId"))::text AS lo,
                           GREATEST(me."UserId", COALESCE(o."BotId", o."UserId"))::text AS hi,
                           ROW_NUMBER() OVER (
                               PARTITION BY LEAST(me."UserId", COALESCE(o."BotId", o."UserId")),
                                            GREATEST(me."UserId", COALESCE(o."BotId", o."UserId"))
                               ORDER BY c."CreatedAt") AS n
                    FROM "Chats" c
                    JOIN "ChatMembers" me ON me."ChatId" = c."Id" AND me."Role" = 'Owner' AND me."UserId" IS NOT NULL
                    JOIN "ChatMembers" o ON o."ChatId" = c."Id" AND o."Id" <> me."Id"
                    JOIN "Users" mu ON mu."Id" = me."UserId"
                    LEFT JOIN "Bots" b ON b."Id" = o."BotId"
                    LEFT JOIN "Users" u ON u."Id" = o."UserId"
                    WHERE (SELECT count(*) FROM "ChatMembers" x WHERE x."ChatId" = c."Id") = 2
                      AND (c."Name" = b."Name"
                           OR c."Name" = COALESCE(NULLIF(mu."DisplayName", ''), mu."Email") || ' & ' || COALESCE(NULLIF(u."DisplayName", ''), u."Email"))
                )
                UPDATE "Chats" c SET "DirectKey" = p.lo || ':' || p.hi
                FROM pairs p
                WHERE p."Id" = c."Id" AND p.n = 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Chats_DirectKey",
                table: "Chats",
                column: "DirectKey",
                unique: true,
                filter: "\"DirectKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Chats_DirectKey",
                table: "Chats");

            migrationBuilder.DropColumn(
                name: "DirectKey",
                table: "Chats");
        }
    }
}
