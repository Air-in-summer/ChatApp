using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MultiRoomChatWebApp.Server.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddUserTrigramIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateIndex(
                name: "IX_Users_DisplayName_Trgm",
                table: "Users",
                column: "DisplayName")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username_Trgm",
                table: "Users",
                column: "Username")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_DisplayName_Trgm",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_Username_Trgm",
                table: "Users");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}
