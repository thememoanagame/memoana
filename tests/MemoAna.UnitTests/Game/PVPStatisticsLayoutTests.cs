using MemoAna.Game.Enums;
using MemoAna.Game.Models;
using Xunit;

namespace MemoAna.UnitTests.Game;

public sealed class PVPStatisticsLayoutTests
{
    [Fact]
    public void GameRazor_DoesNotContainObsoletePvpTableCssOrMarkup()
    {
        // Read Game.razor file from src
        string solutionDir = FindSolutionDir();
        string gameRazorPath = Path.Combine(solutionDir, "src", "MemoAna", "Components", "Pages", "Game.razor");
        Assert.True(File.Exists(gameRazorPath), $"Game.razor not found at {gameRazorPath}");

        string content = File.ReadAllText(gameRazorPath);

        // Verify obsolete table markup and CSS classes are removed
        Assert.DoesNotContain("pvp-statistics-table", content);
        Assert.DoesNotContain("pvp-statistics-container", content);
        Assert.DoesNotContain("<table", content);

        // Verify unified vertical layout with stat comparison is used for PVP
        Assert.Contains("data-testid=\"@(IsPvpMode ? \"pvp-statistics\" : null)\"", content);
        Assert.Contains("data-testid=\"pvp-stat-hits\"", content);
        Assert.Contains("data-testid=\"pvp-stat-misses\"", content);
        Assert.Contains("data-testid=\"pvp-stat-moves\"", content);
        Assert.Contains("data-testid=\"pvp-stat-score\"", content);
        Assert.Contains("data-testid=\"pvp-stat-total\"", content);
        Assert.Contains("stat-player", content);
        Assert.Contains("stat-separator", content);
        Assert.Contains("stat-ai", content);
    }

    [Fact]
    public void PvpStatistics_RoleMapping_IsSymmetricForHostAndClient()
    {
        var hostStats = new PVPPlayerStatistics(PVPPlayerRole.Host, "Alice", Score: 150, Hits: 6, Misses: 2, TotalMoves: 8);
        var clientStats = new PVPPlayerStatistics(PVPPlayerRole.Client, "Bob", Score: 80, Hits: 4, Misses: 4, TotalMoves: 8);
        IReadOnlyList<PVPPlayerStatistics> stats = [hostStats, clientStats];

        // 1. When local player is Host
        PVPPlayerRole hostLocalRole = PVPPlayerRole.Host;
        var hostLocal = stats.FirstOrDefault(s => s.Role == hostLocalRole);
        var hostRemote = stats.FirstOrDefault(s => s.Role != hostLocalRole);

        Assert.NotNull(hostLocal);
        Assert.NotNull(hostRemote);
        Assert.Equal("Alice", hostLocal.Name);
        Assert.Equal(150, hostLocal.Score);
        Assert.Equal(150, hostLocal.FinalScore);
        Assert.Equal(6, hostLocal.Hits);
        Assert.Equal(2, hostLocal.Misses);
        Assert.Equal(8, hostLocal.TotalMoves);

        Assert.Equal("Bob", hostRemote.Name);
        Assert.Equal(80, hostRemote.Score);
        Assert.Equal(80, hostRemote.FinalScore);
        Assert.Equal(4, hostRemote.Hits);
        Assert.Equal(4, hostRemote.Misses);
        Assert.Equal(8, hostRemote.TotalMoves);

        // 2. When local player is Client
        PVPPlayerRole clientLocalRole = PVPPlayerRole.Client;
        var clientLocal = stats.FirstOrDefault(s => s.Role == clientLocalRole);
        var clientRemote = stats.FirstOrDefault(s => s.Role != clientLocalRole);

        Assert.NotNull(clientLocal);
        Assert.NotNull(clientRemote);
        Assert.Equal("Bob", clientLocal.Name);
        Assert.Equal(80, clientLocal.Score);
        Assert.Equal(80, clientLocal.FinalScore);
        Assert.Equal(4, clientLocal.Hits);

        Assert.Equal("Alice", clientRemote.Name);
        Assert.Equal(150, clientRemote.Score);
        Assert.Equal(150, clientRemote.FinalScore);
        Assert.Equal(6, clientRemote.Hits);
    }

    private static string FindSolutionDir()
    {
        string? current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "MemoAna.sln")) ||
                Directory.Exists(Path.Combine(current, "src")))
            {
                return current;
            }
            current = Directory.GetParent(current)?.FullName;
        }

        // Fallback to working directory
        return Directory.GetCurrentDirectory();
    }
}
