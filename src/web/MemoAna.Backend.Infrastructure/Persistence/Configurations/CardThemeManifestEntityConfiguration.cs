using MemoAna.Backend.Domain.Game;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MemoAna.Backend.Infrastructure.Persistence.Configurations;

/// <summary>Maps card theme manifest metadata to PostgreSQL.</summary>
public sealed class CardThemeManifestEntityConfiguration
    : IEntityTypeConfiguration<CardThemeManifestEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CardThemeManifestEntity> builder)
    {
        _ = builder.ToTable("CardThemeManifests");
        _ = builder.HasKey(manifest => manifest.Id);
        _ = builder.Property(manifest => manifest.Id)
            .ValueGeneratedNever();
        _ = builder.Property(manifest => manifest.ThemeName)
            .IsRequired();
        _ = builder.Property(manifest => manifest.CardThemeId)
            .IsRequired();
        _ = builder.Property(manifest => manifest.PreviewAssetId)
            .IsRequired();
        _ = builder.HasIndex(manifest => manifest.CardThemeId)
            .IsUnique();
        _ = builder.HasIndex(manifest => manifest.PreviewAssetId);
        _ = builder.Property(manifest => manifest.CreatedBy)
            .IsRequired();
        _ = builder.Property(manifest => manifest.UpdatedBy)
            .IsRequired();
    }
}
