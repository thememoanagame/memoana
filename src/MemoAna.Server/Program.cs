using Mediator;
using MemoAna.Server.Infrastructure.Persistence;
using MemoAna.Server.Application.Abstract;
using MemoAna.Server.Infrastructure.Services.Concrete;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.OpenApi;
using Scalar.AspNetCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddDbContext<ServerDbContext>(options =>
{
    options.UseSqlite("Data Source=server.db;Cache=Shared;Foreign Keys=True;");
});
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();
builder.Services.AddIdentity<IdentityUser, IdentityRole>()
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ServerDbContext>()
    .AddDefaultTokenProviders();

builder.Services.Configure<IdentityOptions>(options =>
{
    // Password settings.
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequireUppercase = true;
    options.Password.RequiredLength = 6;
    options.Password.RequiredUniqueChars = 1;

    // Lockout settings.
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;

    // User settings.
    options.User.AllowedUserNameCharacters =
    "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+";
    options.User.RequireUniqueEmail = false;
});
builder.Services.AddSingleton<IRelayService, RelayService>();
builder.Services.AddMediator(options =>
{
    options.Assemblies = [typeof(MemoAna.Server.Application.Relay.Handlers.RelayServerHandlers).Assembly];
    options.ServiceLifetime = ServiceLifetime.Scoped;
});
var app = builder.Build();

if (!app.Environment.IsProduction())
{
    app.MapOpenApi("openapi/{v1}.json");
    app.MapScalarApiReference("scalar/server", options =>
    {
        options.WithTitle($"{app.Environment.ApplicationName} API at {app.Environment.EnvironmentName} v1")
            .WithTheme(ScalarTheme.Moon);
    });
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/api/v1/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";

        var response = new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                durationMs = entry.Value.Duration.TotalMilliseconds,
                error = entry.Value.Exception?.Message
            })
        };

        await context.Response.WriteAsJsonAsync(response);
    }
});
await app.RunAsync();
