using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MemoAna.Common.Persistence.Migrations;

public partial class AddGameStatisticsIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PlayerName",
            table: "GameStatistics",
            type: "TEXT",
            nullable: false,
            defaultValue: "Player");

        migrationBuilder.AddColumn<int>(
            name: "GameMode",
            table: "GameStatistics",
            type: "INTEGER",
            nullable: false,
            defaultValue: 1);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "PlayerName", table: "GameStatistics");
        migrationBuilder.DropColumn(name: "GameMode", table: "GameStatistics");
    }
}
