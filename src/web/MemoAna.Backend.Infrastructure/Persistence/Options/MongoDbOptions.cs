namespace MemoAna.Backend.Infrastructure.Persistence.Options;

/// <summary>Configures the MongoDB persistence store.</summary>
public sealed class MongoDbOptions
{
    public const string SectionName = "MongoDb";

    public required string ConnectionString { get; set; }

    public required string Database { get; set; }

    public string CardThemeAssetsCollection { get; set; } =
        "card-theme-assets";
}
