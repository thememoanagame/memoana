namespace MemoAna.Backend.Infrastructure.Persistence.Options;

public sealed class ConnectionStringsOptions
{
    public const string SectionName = "ConnectionStrings";

    public required string MemoAna { get; set; }
}
