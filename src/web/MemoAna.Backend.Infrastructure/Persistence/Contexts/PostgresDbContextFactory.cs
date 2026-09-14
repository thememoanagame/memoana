using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace MemoAna.Backend.Infrastructure.Persistence.Contexts;

public class PostgresDbContextFactory : IDesignTimeDbContextFactory<PostgresDbContext>
{
    public PostgresDbContext CreateDbContext(string[] args)
    {
        string basePath = Directory.GetCurrentDirectory();

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.Development.json", optional: true)
            .AddEnvironmentVariables() 
            .Build();

        string? cs = configuration.GetConnectionString("MemoAna");

        if (string.IsNullOrEmpty(cs))
        {
            throw new InvalidOperationException("The 'MemoAna' ConnectionString was not found at appsettings.json.");
        }

        return new PostgresDbContext(
            new DbContextOptionsBuilder<PostgresDbContext>()
            .UseNpgsql(cs)
            .Options);
    }
}
