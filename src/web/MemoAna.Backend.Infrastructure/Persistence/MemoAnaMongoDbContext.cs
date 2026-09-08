using MemoAna.Backend.Domain.Game;
using MemoAna.Backend.Infrastructure.Persistence.Options;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace MemoAna.Backend.Infrastructure.Persistence;

/// <summary>
/// Provides access to the configured MongoDB database and supported collections.
/// </summary>
public sealed class MemoAnaMongoDbContext
{
    private readonly MongoDbOptions options;
    private readonly IMongoDatabase database;

    public MemoAnaMongoDbContext(IOptions<MongoDbOptions> options)
    {
        this.options = options.Value;
        ArgumentException.ThrowIfNullOrWhiteSpace(
            this.options.ConnectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(this.options.Database);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            this.options.CardThemeAssetsCollection);

        RegisterMappings();
        MongoClient client = new(this.options.ConnectionString);
        database = client.GetDatabase(this.options.Database);
    }

    /// <summary>Gets the configured collection for a supported document type.</summary>
    public IMongoCollection<TEntity> GetCollection<TEntity>()
        where TEntity : class
    {
        if (typeof(TEntity) != typeof(CardThemeAssetEntity))
        {
            throw new InvalidOperationException(
                $"MongoDB persistence is not configured for {typeof(TEntity).FullName}.");
        }

        return database.GetCollection<TEntity>(
            options.CardThemeAssetsCollection);
    }

    /// <summary>Creates indexes required by the supported document collections.</summary>
    public async Task EnsureIndexesAsync(
        CancellationToken cancellationToken = default)
    {
        IMongoCollection<CardThemeAssetEntity> collection =
            GetCollection<CardThemeAssetEntity>();

        IndexKeysDefinition<CardThemeAssetEntity> keys =
            Builders<CardThemeAssetEntity>.IndexKeys
                .Ascending(asset => asset.CardThemeId);
        CreateIndexOptions indexOptions = new()
        {
            Name = "IX_CardThemeAssets_CardThemeId"
        };

        _ = await collection.Indexes.CreateOneAsync(
            new CreateIndexModel<CardThemeAssetEntity>(
                keys,
                indexOptions),
            cancellationToken: cancellationToken);
    }

    private static void RegisterMappings()
    {
        if (BsonClassMap.IsClassMapRegistered(
            typeof(CardThemeAssetEntity)))
        {
            return;
        }

        BsonClassMap.RegisterClassMap<CardThemeAssetEntity>(
            map =>
            {
                map.AutoMap();
                _ = map.MapIdMember(asset => asset.Id)
                    .SetElementName("_id");
            });
    }
}
