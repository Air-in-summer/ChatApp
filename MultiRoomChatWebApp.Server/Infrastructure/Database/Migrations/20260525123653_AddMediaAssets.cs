using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MultiRoomChatWebApp.Server.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MediaAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AccessLevel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RoomId = table.Column<Guid>(type: "uuid", nullable: true),
                    MessageId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    BucketName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ChecksumSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Width = table.Column<int>(type: "integer", nullable: true),
                    Height = table.Column<int>(type: "integer", nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: true),
                    PublicUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ThumbnailMediaId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AttachedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaAssets", x => x.Id);
                    table.CheckConstraint("CK_MediaAssets_SizeBytes_NonNegative", "\"SizeBytes\" >= 0");
                    table.ForeignKey(
                        name: "FK_MediaAssets_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_BucketName_StorageKey",
                table: "MediaAssets",
                columns: new[] { "BucketName", "StorageKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_OwnerUserId_CreatedAt",
                table: "MediaAssets",
                columns: new[] { "OwnerUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_RoomId_CreatedAt",
                table: "MediaAssets",
                columns: new[] { "RoomId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_Scope_OwnerUserId",
                table: "MediaAssets",
                columns: new[] { "Scope", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_Status_CreatedAt",
                table: "MediaAssets",
                columns: new[] { "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaAssets");
        }
    }
}
