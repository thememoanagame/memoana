using MemoAna.Backend.Domain.Game;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MemoAna.Backend.Infrastructure.Persistence.Configurations;

/// <summary>Maps card theme catalog metadata to PostgreSQL.</summary>
public sealed class CardThemeEntityConfiguration
    : IEntityTypeConfiguration<CardThemeEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CardThemeEntity> builder)
    {
        _ = builder.ToTable("CardThemes");
        _ = builder.HasKey(theme => theme.Id);
        _ = builder.Property(theme => theme.Id)
            .ValueGeneratedNever();
        _ = builder.Property(theme => theme.ManifestId)
            .IsRequired();
        _ = builder.HasIndex(theme => theme.ManifestId);
        _ = builder.Property(theme => theme.CreatedBy)
            .IsRequired();
        _ = builder.Property(theme => theme.UpdatedBy)
            .IsRequired();
    }
}
