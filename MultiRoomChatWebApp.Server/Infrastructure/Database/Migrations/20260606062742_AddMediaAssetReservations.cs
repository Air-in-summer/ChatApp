using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MultiRoomChatWebApp.Server.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaAssetReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReservedAt",
                table: "MediaAssets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReservedByMessageId",
                table: "MediaAssets",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_Status_ReservedAt",
                table: "MediaAssets",
                columns: new[] { "Status", "ReservedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MediaAssets_Status_ReservedAt",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "ReservedAt",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "ReservedByMessageId",
                table: "MediaAssets");
        }
    }
}
