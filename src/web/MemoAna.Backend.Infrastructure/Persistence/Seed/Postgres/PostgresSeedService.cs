using System.Globalization;
using System.Security.Claims;
using System.Text;
using MemoAna.Backend.Application.Common.Abstractions;
using MemoAna.Backend.Application.Common.Contracts;
using MemoAna.Backend.Application.Seed;
using MemoAna.Backend.Application.Seed.Abstractions;
using MemoAna.Backend.Application.Seed.Dtos;
using MemoAna.Backend.Domain.Game;
using MemoAna.Backend.Infrastructure.Identity.Models;
using MemoAna.Backend.Infrastructure.Persistence.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace MemoAna.Backend.Infrastructure.Persistence.Seed.Postgres;

/// <summary>
/// Seeds Identity and the relational references shared with MongoDB card assets.
/// </summary>
public sealed class PostgresSeedService(
    PostgresDbContext dbContext,
    UserManager<User> userManager,
    RoleManager<Role> roleManager,
    INoRepository<CardThemeAssetEntity> assetRepository,
    ICardThemeAssetSeedService cardThemeAssetSeedService,
    IConfiguration configuration,
    ILogger<PostgresSeedService> logger) : IPostgresSeedService
{
    private readonly string SeedUserEmail = configuration["Seed:App:User"]! ?? throw new ArgumentNullException("appuser");
    private readonly string SeedActor = configuration["Seed:App:Actor"]!;

    private static readonly ThemeDefinition[] Themes =
    [
        new("disney", "theme-disney", true, ["disney"]),
        new("marvel", "theme-marvel", false, ["marvel"]),
        new("pokemon", "theme-pokemon", false, ["pokemon", "pokémon"]),
        new("cars", "theme-cars", false, ["cars", "píxar", "pixar"])
    ];

    private static readonly RoleDefinition[] Roles =
    [
        new(IdentityRoles.Administrator, "system.admin"),
        new(IdentityRoles.User, "system.user")
    ];

    /// <inheritdoc />
    public async Task<SeedStatusDto> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        User? user = await userManager.FindByEmailAsync(SeedUserEmail);
        bool applicationUserExists = user is not null;
        bool administratorRoleLinked = applicationUserExists
            && await userManager.IsInRoleAsync(user!, IdentityRoles.Administrator);
        bool rolesExist = await AreRolesReadyAsync(cancellationToken);
        bool themesLinked = await AreThemesLinkedAsync(cancellationToken);

        List<string> missing = [];
        if (!applicationUserExists)
        {
            missing.Add("Application user is missing.");
        }

        if (!rolesExist)
        {
            missing.Add("One or more application roles or permissions are missing.");
        }

        if (!administratorRoleLinked)
        {
            missing.Add("The seed user is not linked to the administrator role.");
        }

        if (!themesLinked)
        {
            missing.Add("One or more PostgreSQL themes are not linked to MongoDB assets.");
        }

        return new SeedStatusDto(
            applicationUserExists,
            rolesExist,
            administratorRoleLinked,
            themesLinked,
            missing.Count > 0,
            missing);
    }

    /// <inheritdoc />
    public async Task<SeedOperationResultDto> SeedAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            // MongoDB is seeded first because the relational records reference these stable IDs.
            await cardThemeAssetSeedService.SeedAsync(cancellationToken);
            SeedStatusDto currentStatus = await GetStatusAsync(cancellationToken);
            if (!currentStatus.SeedRequired)
            {
                throw new SeedException(
                    "The seed middleware is no longer available because the application is initialized.");
            }

            List<string> rolesCreated = [];
            List<string> themesCreated = [];
            List<string> themesCorrected = [];
            bool userCreated = await EnsureIdentityAsync(
                rolesCreated,
                cancellationToken);

            foreach (ThemeDefinition definition in Themes)
            {
                ThemeSeedOutcome outcome = await EnsureThemeAsync(
                    definition,
                    cancellationToken);

                if (outcome.Created)
                {
                    themesCreated.Add(definition.Id);
                }

                if (outcome.Corrected)
                {
                    themesCorrected.Add(definition.Id);
                }
            }

            _ = await dbContext.SaveChangesAsync(cancellationToken);
            SeedStatusDto finalStatus = await GetStatusAsync(cancellationToken);
            if (finalStatus.SeedRequired)
            {
                throw new SeedException("The seed completed but the application is still missing prerequisites.", finalStatus.MissingRequirements);
            }

            logger.LogInformation(
                "Application seed completed. UserCreated={UserCreated}, RolesCreated={RolesCreated}, ThemesCreated={ThemesCreated}, ThemesCorrected={ThemesCorrected}",
                userCreated,
                rolesCreated.Count,
                themesCreated.Count,
                themesCorrected.Count);

            return new SeedOperationResultDto(
                userCreated,
                rolesCreated,
                themesCreated,
                themesCorrected,
                finalStatus);
        }
        catch (InvalidOperationException exception)
        {
            throw new SeedException(
                "The application seed could not be completed.",
                [exception.Message],
                exception);
        }
        catch (DbUpdateException exception)
        {
            throw new SeedException(
                "The application seed could not persist PostgreSQL data.",
                [exception.GetBaseException().Message],
                exception);
        }
        catch (MongoException exception)
        {
            throw new SeedException(
                "The application seed could not persist MongoDB assets.",
                [exception.GetBaseException().Message],
                exception);
        }
    }

    private async Task<bool> EnsureIdentityAsync(
        ICollection<string> rolesCreated,
        CancellationToken cancellationToken)
    {
        foreach (RoleDefinition definition in Roles)
        {
            Role? role = await roleManager.FindByNameAsync(definition.Name);
            if (role is null)
            {
                role = new Role(definition.Name);
                IdentityResult createRoleResult = await roleManager.CreateAsync(role);
                EnsureIdentitySuccess(
                    createRoleResult,
                    $"Could not create role '{definition.Name}'.");
                rolesCreated.Add(definition.Name);
            }

            IList<Claim> claims = await roleManager.GetClaimsAsync(role);
            if (!claims.Any(claim =>
                    claim.Type == IdentityClaimTypes.Permission
                    && claim.Value == definition.Permission))
            {
                IdentityResult claimResult = await roleManager.AddClaimAsync(
                    role,
                    new Claim(IdentityClaimTypes.Permission, definition.Permission));
                EnsureIdentitySuccess(
                    claimResult,
                    $"Could not configure role '{definition.Name}'.");
            }
        }

        User? user = await userManager.FindByEmailAsync(SeedUserEmail);
        bool created = user is null;
        if (user is null)
        {
            user = new User(SeedUserEmail)
            {
                Email = SeedUserEmail,
                EmailConfirmed = true,
                DisplayName = SeedUserEmail,
                CreatedBy = SeedActor,
                UpdatedBy = SeedActor
            };
            IdentityResult createUserResult = await userManager.CreateAsync(user);
            EnsureIdentitySuccess(
                createUserResult,
                $"Could not create application user '{SeedUserEmail}'.");
        }

        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            IdentityResult updateUserResult = await userManager.UpdateAsync(user);
            EnsureIdentitySuccess(
                updateUserResult,
                $"Could not confirm application user '{SeedUserEmail}'.");
        }

        if (!await userManager.IsInRoleAsync(user, IdentityRoles.Administrator))
        {
            IdentityResult roleResult = await userManager.AddToRoleAsync(
                user,
                IdentityRoles.Administrator);
            EnsureIdentitySuccess(
                roleResult,
                $"Could not link '{SeedUserEmail}' to the administrator role.");
        }

        return created;
    }

    private async Task<bool> AreRolesReadyAsync(
        CancellationToken cancellationToken)
    {
        foreach (RoleDefinition definition in Roles)
        {
            Role? role = await roleManager.FindByNameAsync(definition.Name);
            if (role is null)
            {
                return false;
            }

            IList<Claim> claims = await roleManager.GetClaimsAsync(role);
            if (!claims.Any(claim =>
                    claim.Type == IdentityClaimTypes.Permission
                    && claim.Value == definition.Permission))
            {
                return false;
            }
        }

        await dbContext.Database.CanConnectAsync(cancellationToken);
        return true;
    }

    private async Task<bool> AreThemesLinkedAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CardThemeEntity> themes = await dbContext.CardThemes
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        IReadOnlyList<CardThemeManifestEntity> manifests =
            await dbContext.CardThemeManifests
                .AsNoTracking()
                .ToListAsync(cancellationToken);

        foreach (ThemeDefinition definition in Themes)
        {
            CardThemeEntity? theme = themes.FirstOrDefault(
                candidate => candidate.Id == definition.Id);
            CardThemeManifestEntity? manifest = theme is null
                ? null
                : manifests.FirstOrDefault(
                    candidate => candidate.Id == theme.ManifestId);
            CardThemeAssetEntity? asset = await assetRepository.FirstOrDefaultAsync(
                candidate => candidate.CardThemeId == definition.Id,
                cancellationToken);

            if (theme is null
                || manifest is null
                || manifest.CardThemeId != definition.Id
                || manifest.PreviewAssetId != definition.Id
                || asset is null
                || asset.Id != definition.Id)
            {
                return false;
            }
        }

        return true;
    }

    private async Task<ThemeSeedOutcome> EnsureThemeAsync(
        ThemeDefinition definition,
        CancellationToken cancellationToken)
    {
        CardThemeEntity? theme = await dbContext.CardThemes
            .FirstOrDefaultAsync(candidate => candidate.Id == definition.Id, cancellationToken);
        CardThemeManifestEntity? manifest = theme is null
            ? null
            : await dbContext.CardThemeManifests
                .FirstOrDefaultAsync(
                    candidate => candidate.Id == theme.ManifestId,
                    cancellationToken);
        bool corrected = false;
        bool created = false;

        if (manifest is null)
        {
            manifest = await dbContext.CardThemeManifests
                .FirstOrDefaultAsync(
                    candidate => candidate.CardThemeId == definition.Id,
                    cancellationToken);
            manifest ??= (await dbContext.CardThemeManifests
                .ToListAsync(cancellationToken))
                .FirstOrDefault(candidate => MatchesThemeName(
                    candidate.ThemeName,
                    definition));
        }

        if (theme is null && manifest is not null)
        {
            theme = await dbContext.CardThemes
                .FirstOrDefaultAsync(
                    candidate => candidate.ManifestId == manifest.Id,
                    cancellationToken);
            if (theme is not null && theme.Id != definition.Id)
            {
                dbContext.CardThemes.Remove(theme);
                theme = new CardThemeEntity(definition.Id)
                {
                    ManifestId = manifest.Id,
                    CreatedBy = SeedActor,
                    UpdatedBy = SeedActor
                };
                _ = dbContext.CardThemes.Add(theme);
                corrected = true;
            }
        }

        if (manifest is null)
        {
            string manifestId = $"{definition.Id}-manifest";
            manifest = await dbContext.CardThemeManifests
                .FirstOrDefaultAsync(
                    candidate => candidate.Id == manifestId,
                    cancellationToken)
                ?? new CardThemeManifestEntity(manifestId)
                {
                    CreatedBy = SeedActor,
                    UpdatedBy = SeedActor
                };
            _ = dbContext.CardThemeManifests.Add(manifest);
            created = true;
        }

        if (theme is null)
        {
            theme = new CardThemeEntity(definition.Id)
            {
                CreatedBy = SeedActor,
                UpdatedBy = SeedActor
            };
            _ = dbContext.CardThemes.Add(theme);
            created = true;
        }

        if (theme.ManifestId != manifest.Id
            || manifest.CardThemeId != definition.Id
            || manifest.PreviewAssetId != definition.Id)
        {
            corrected = true;
        }

        theme.ManifestId = manifest.Id;
        theme.UpdatedBy = SeedActor;
        manifest.CardThemeId = definition.Id;
        manifest.PreviewAssetId = definition.Id;
        manifest.ThemeName = definition.Name;
        manifest.IsDefault = definition.IsDefault;
        manifest.UpdatedBy = SeedActor;

        return new ThemeSeedOutcome(created, corrected);
    }

    private static bool MatchesThemeName(
        string themeName,
        ThemeDefinition definition)
    {
        string normalized = Normalize(themeName);
        return definition.Names.Any(name => Normalize(name) == normalized);
    }

    private static string Normalize(string value)
    {
        string decomposed = value.Normalize(NormalizationForm.FormD);
        StringBuilder builder = new();
        foreach (char character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character)
                != UnicodeCategory.NonSpacingMark)
            {
                _ = builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static void EnsureIdentitySuccess(
        IdentityResult result,
        string message)
    {
        if (result.Succeeded)
        {
            return;
        }

        throw new SeedException(
            message,
            result.Errors.Select(error => error.Description).ToArray());
    }

    private sealed record RoleDefinition(string Name, string Permission);

    private sealed record ThemeDefinition(
        string Name,
        string Id,
        bool IsDefault,
        IReadOnlyList<string> Names);

    private sealed record ThemeSeedOutcome(bool Created, bool Corrected);
}
