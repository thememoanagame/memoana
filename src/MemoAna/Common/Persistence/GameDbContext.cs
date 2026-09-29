using MemoAna.Game.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Text.Json;

namespace MemoAna.Common.Persistence;

public partial class GameDbContext(DbContextOptions<GameDbContext> options) : DbContext(options)
{
    public DbSet<CardThemeEntity> CardThemes => Set<CardThemeEntity>();
    public DbSet<CardThemeManifestEntity> CardThemeManifests => Set<CardThemeManifestEntity>();
    public DbSet<GameSettingsEntity> GameSettings => Set<GameSettingsEntity>();
    public DbSet<GameStatisticsEntity> GameStatistics => Set<GameStatisticsEntity>();
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<CardThemeManifestEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ThemeName).IsRequired();
            entity.Property(e => e.PreviewBase64Image).IsRequired();
            entity.HasOne(m => m.CardTheme)
                  .WithOne(t => t.Manifest)
                  .HasForeignKey<CardThemeEntity>(t => t.ManifestId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CardThemeEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Base64Images)
                  .HasConversion(new ValueConverter<List<string>, string>(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()
            )).IsRequired();
        });

        builder.Entity<GameStatisticsEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ThemeName).IsRequired();
            entity.Property(e => e.Difficulty).IsRequired();
            entity.Property(e => e.PlayedAt).IsRequired();
            entity.Property(e => e.IsVictory).IsRequired();
            entity.Property(e => e.TotalMoves).IsRequired();
            entity.Property(e => e.SuccessfulMoves).IsRequired();
            entity.Property(e => e.Mistakes).IsRequired();
            entity.Property(e => e.RemainingSeconds).IsRequired();
            entity.Property(e => e.FinalScore).IsRequired();
        });

        builder.Entity<GameSettingsEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id");
            entity.OwnsOne(e => e.Options, options => options.ToJson());
        });
    }
}
