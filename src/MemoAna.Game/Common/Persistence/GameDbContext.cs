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
    public DbSet<RemoteThemeCardEntity> RemoteThemeCards => Set<RemoteThemeCardEntity>();
    public DbSet<RemoteThemeIndexEntity> RemoteThemeIndexes => Set<RemoteThemeIndexEntity>();
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<CardThemeManifestEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ThemeName).IsRequired();
            entity.Property(e => e.ThumbnailSrc).IsRequired();
            entity.HasOne(m => m.CardTheme)
                  .WithOne(t => t.Manifest)
                  .HasForeignKey<CardThemeEntity>(t => t.ManifestId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CardThemeEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ImagesSrc)
                  .HasConversion(new ValueConverter<List<string>, string>(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()
            )).IsRequired();
        });

        builder.Entity<GameStatisticsEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ThemeName).IsRequired();
            entity.Property(e => e.PlayerName).IsRequired();
            entity.Property(e => e.GameMode).IsRequired();
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

        builder.Entity<RemoteThemeIndexEntity>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name).IsRequired().HasMaxLength(150);
            entity.Property(e => e.CollectionPath).IsRequired().HasMaxLength(300);
            entity.Property(e => e.ThumbnailPath).IsRequired().HasMaxLength(300);
            entity.Property(e => e.Hash).IsRequired().HasMaxLength(100);

            entity.HasMany(e => e.Cards)
                .WithOne(c => c.RemoteThemeIndex)
                .HasForeignKey(c => c.RemoteThemeIndexId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RemoteThemeCardEntity>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.CardPath).IsRequired().HasMaxLength(300);
            entity.Property(e => e.NameEnUs).HasMaxLength(150);
            entity.Property(e => e.NamePtBr).HasMaxLength(150);
        });
    }
}
