using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MultiRoomChatWebApp.Server.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class HashRefreshTokenStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Token",
                table: "RefreshTokens",
                newName: "TokenHash");

            migrationBuilder.RenameIndex(
                name: "IX_RefreshTokens_Token",
                table: "RefreshTokens",
                newName: "IX_RefreshTokens_TokenHash");

            // Backfill token cu sang SHA-256 Base64 de DB khong con luu raw refresh token.
            migrationBuilder.Sql(@"CREATE EXTENSION IF NOT EXISTS pgcrypto;");
            migrationBuilder.Sql(@"UPDATE ""RefreshTokens"" SET ""TokenHash"" = encode(digest(""TokenHash"", 'sha256'), 'base64');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Khong the khoi phuc raw token tu hash, nen rollback se dang xuat cac phien cu.
            migrationBuilder.Sql(@"DELETE FROM ""RefreshTokens"";");

            migrationBuilder.RenameColumn(
                name: "TokenHash",
                table: "RefreshTokens",
                newName: "Token");

            migrationBuilder.RenameIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "RefreshTokens",
                newName: "IX_RefreshTokens_Token");
        }
    }
}
