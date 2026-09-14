using MemoAna.Backend.Domain.Game;
using MemoAna.Backend.Infrastructure.Persistence.Options;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace MemoAna.Backend.Infrastructure.Persistence.Contexts;

/// <summary>
/// Provides access to the configured MongoDB database and supported collections.
/// </summary>
public sealed class MongoDbContext 
{
    private readonly ConnectionStringsOptions options;
    private readonly MongoDbOptions mongoOptions;
    private readonly IMongoDatabase database;

    public MongoDbContext(IOptions<MongoDbOptions> mongoOptions, IOptions<ConnectionStringsOptions> options)
    {
        this.mongoOptions = mongoOptions.Value;
        this.options = options.Value;
        ArgumentException.ThrowIfNullOrWhiteSpace(
            this.options.Mongo);
        ArgumentException.ThrowIfNullOrWhiteSpace(this.mongoOptions.Database);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            this.mongoOptions.CardThemeAssetsCollection);

        RegisterMappings();
        MongoClient client = new(this.options.Mongo);
        database = client.GetDatabase(this.mongoOptions.Database);
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
            mongoOptions.CardThemeAssetsCollection);
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
