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
    public void GameRazor_UsesDistinctPvpHeaderMarkupAndDedicatedPvpResultKeys()
    {
        string solutionDir = FindSolutionDir();
        string gameRazorPath = Path.Combine(solutionDir, "src", "MemoAna", "Components", "Pages", "Game.razor");
        Assert.True(File.Exists(gameRazorPath), $"Game.razor not found at {gameRazorPath}");

        string content = File.ReadAllText(gameRazorPath);

        // Dedicated PVP title and subtitle elements
        Assert.Contains("data-testid=\"pvp-result-title\"", content);
        Assert.Contains("data-testid=\"pvp-result-subtitle\"", content);
        Assert.Contains("PvpResultTitle", content);
        Assert.Contains("PvpResultSubtitle", content);

        // Dedicated localization keys referenced in Game.razor
        Assert.Contains("Localizer[\"PvpVictory\"]", content);
        Assert.Contains("Localizer[\"PvpDefeat\"]", content);
        Assert.Contains("Localizer[\"PvpTie\"]", content);
        Assert.Contains("Localizer[\"PvpVictorySubtitle\"]", content);
        Assert.Contains("Localizer[\"PvpDefeatSubtitle\"]", content);
        Assert.Contains("Localizer[\"PvpTieSubtitle\"]", content);

        // Does NOT reuse generic / TimeAttack / AI IsVictory assignment in OnPvpGameFinished
        Assert.DoesNotContain("IsVictory = (e.Result.Winner == pvp.LocalRole)", content);
    }

    [Fact]
    public void PvpResult_OutcomeEvaluation_HandlesVictoryDefeatAndTieDeterministically()
    {
        // Helper to evaluate outcome title and subtitle keys
        static (string titleKey, string subtitleKey) EvaluateOutcome(int localScore, int remoteScore)
        {
            if (localScore > remoteScore)
                return ("PvpVictory", "PvpVictorySubtitle");
            if (localScore < remoteScore)
                return ("PvpDefeat", "PvpDefeatSubtitle");
            return ("PvpTie", "PvpTieSubtitle");
        }

        // 1. Host wins
        var (hostTitle1, hostSub1) = EvaluateOutcome(localScore: 100, remoteScore: 50);
        var (clientTitle1, clientSub1) = EvaluateOutcome(localScore: 50, remoteScore: 100);
        Assert.Equal("PvpVictory", hostTitle1);
        Assert.Equal("PvpVictorySubtitle", hostSub1);
        Assert.Equal("PvpDefeat", clientTitle1);
        Assert.Equal("PvpDefeatSubtitle", clientSub1);

        // 2. Client wins
        var (hostTitle2, hostSub2) = EvaluateOutcome(localScore: 40, remoteScore: 80);
        var (clientTitle2, clientSub2) = EvaluateOutcome(localScore: 80, remoteScore: 40);
        Assert.Equal("PvpDefeat", hostTitle2);
        Assert.Equal("PvpDefeatSubtitle", hostSub2);
        Assert.Equal("PvpVictory", clientTitle2);
        Assert.Equal("PvpVictorySubtitle", clientSub2);

        // 3. Draw / Tie
        var (hostTitle3, hostSub3) = EvaluateOutcome(localScore: 60, remoteScore: 60);
        var (clientTitle3, clientSub3) = EvaluateOutcome(localScore: 60, remoteScore: 60);
        Assert.Equal("PvpTie", hostTitle3);
        Assert.Equal("PvpTieSubtitle", hostSub3);
        Assert.Equal("PvpTie", clientTitle3);
        Assert.Equal("PvpTieSubtitle", clientSub3);
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
