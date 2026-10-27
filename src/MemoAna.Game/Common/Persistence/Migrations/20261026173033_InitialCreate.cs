using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MemoAna.Common.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CardThemeManifests",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ThemeName = table.Column<string>(type: "TEXT", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    ThumbnailSrc = table.Column<string>(type: "TEXT", nullable: false),
                    CardThemeId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardThemeManifests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GameSettings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Options = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GameStatistics",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ThemeName = table.Column<string>(type: "TEXT", nullable: false),
                    PlayerName = table.Column<string>(type: "TEXT", nullable: false),
                    GameMode = table.Column<int>(type: "INTEGER", nullable: false),
                    Difficulty = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsVictory = table.Column<bool>(type: "INTEGER", nullable: false),
                    RemainingSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalMoves = table.Column<int>(type: "INTEGER", nullable: false),
                    SuccessfulMoves = table.Column<int>(type: "INTEGER", nullable: false),
                    Mistakes = table.Column<int>(type: "INTEGER", nullable: false),
                    FinalScore = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameStatistics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RemoteThemeIndexes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    CollectionPath = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    ThumbnailPath = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Count = table.Column<int>(type: "INTEGER", nullable: false),
                    Hash = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemoteThemeIndexes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CardThemes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ImagesSrc = table.Column<string>(type: "TEXT", nullable: false),
                    ManifestId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardThemes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardThemes_CardThemeManifests_ManifestId",
                        column: x => x.ManifestId,
                        principalTable: "CardThemeManifests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RemoteThemeCards",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    CardPath = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    NameEnUs = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    NamePtBr = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    RemoteThemeIndexId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemoteThemeCards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RemoteThemeCards_RemoteThemeIndexes_RemoteThemeIndexId",
                        column: x => x.RemoteThemeIndexId,
                        principalTable: "RemoteThemeIndexes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CardThemes_ManifestId",
                table: "CardThemes",
                column: "ManifestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RemoteThemeCards_RemoteThemeIndexId",
                table: "RemoteThemeCards",
                column: "RemoteThemeIndexId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CardThemes");

            migrationBuilder.DropTable(
                name: "GameSettings");

            migrationBuilder.DropTable(
                name: "GameStatistics");

            migrationBuilder.DropTable(
                name: "RemoteThemeCards");

            migrationBuilder.DropTable(
                name: "CardThemeManifests");

            migrationBuilder.DropTable(
                name: "RemoteThemeIndexes");
        }
    }
}
