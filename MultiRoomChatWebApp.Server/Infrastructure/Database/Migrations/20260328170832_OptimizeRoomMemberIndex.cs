using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MultiRoomChatWebApp.Server.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeRoomMemberIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RoomMembers_UserId",
                table: "RoomMembers");

            migrationBuilder.CreateIndex(
                name: "IX_RoomMembers_UserId_RoomId",
                table: "RoomMembers",
                columns: new[] { "UserId", "RoomId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RoomMembers_UserId_RoomId",
                table: "RoomMembers");

            migrationBuilder.CreateIndex(
                name: "IX_RoomMembers_UserId",
                table: "RoomMembers",
                column: "UserId");
        }
    }
}
