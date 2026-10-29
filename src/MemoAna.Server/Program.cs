using MemoAna.Server.Infrastructure.Persistence;
using MemoAna.Server.Infrastructure.Services.Abstract;
using MemoAna.Server.Infrastructure.Services.Concrete;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.OpenApi;
using Scalar.AspNetCore;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddDbContext<ServerDbContext>(options =>
{
    options.UseSqlite("Data Source=server.db;Cache=Shared;Foreign Keys=True;");
});
builder.Services.AddOpenApi();
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();
builder.Services.AddIdentity<IdentityUser, IdentityRole>()
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ServerDbContext>()
    .AddDefaultTokenProviders();
builder.Services.AddSingleton<IRelayService, RelayService>();

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

await app.RunAsync();
