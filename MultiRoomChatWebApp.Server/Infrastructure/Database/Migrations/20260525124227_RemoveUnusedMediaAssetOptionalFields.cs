using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MultiRoomChatWebApp.Server.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUnusedMediaAssetOptionalFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChecksumSha256",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "DurationMs",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "Height",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "ThumbnailMediaId",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "Width",
                table: "MediaAssets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChecksumSha256",
                table: "MediaAssets",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationMs",
                table: "MediaAssets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Height",
                table: "MediaAssets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ThumbnailMediaId",
                table: "MediaAssets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Width",
                table: "MediaAssets",
                type: "integer",
                nullable: true);
        }
    }
}
