using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MemoAna.Backend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGameCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CardThemeManifests",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ThemeName = table.Column<string>(type: "text", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CardThemeId = table.Column<string>(type: "text", nullable: false),
                    PreviewAssetId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardThemeManifests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CardThemes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ManifestId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardThemes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CardThemeManifests_CardThemeId",
                table: "CardThemeManifests",
                column: "CardThemeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CardThemeManifests_PreviewAssetId",
                table: "CardThemeManifests",
                column: "PreviewAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_CardThemes_ManifestId",
                table: "CardThemes",
                column: "ManifestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CardThemeManifests");

            migrationBuilder.DropTable(
                name: "CardThemes");
        }
    }
}
