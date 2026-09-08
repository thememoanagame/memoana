using FluentValidation;
using Infisical.Sdk;
using Infisical.Sdk.Model;
using MemoAna.Backend.Application.Common.Abstractions;
using MemoAna.Backend.Application.Common.Contracts;
using MemoAna.Backend.Application.Common.Pipeline.Validation;
using MemoAna.Backend.Application.Health.Abstractions;
using MemoAna.Backend.Application.Identity.Abstractions;
using MemoAna.Backend.Application.Identity.Handlers;
using MemoAna.Backend.Application.Identity.Validators;
using MemoAna.Backend.Infrastructure.Common.HealthChecks;
using MemoAna.Backend.Infrastructure.Common.Repository;
using MemoAna.Backend.Infrastructure.Common.Services;
using MemoAna.Backend.Infrastructure.Common.UnitOfWork;
using MemoAna.Backend.Infrastructure.Identity.Models;
using MemoAna.Backend.Infrastructure.Identity.Options;
using MemoAna.Backend.Infrastructure.Identity.Services;
using MemoAna.Backend.Infrastructure.Persistence;
using MemoAna.Backend.Infrastructure.Persistence.Middlewares;
using MemoAna.Backend.Infrastructure.Persistence.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using System.IdentityModel.Tokens.Jwt;
using System.Text;

namespace MemoAna.Backend.Composition.Extensions;

/// <summary>Adds MemoAna.Backend services to the web host.</summary>
public static class WebApplicationBuilderExtensions
{
    extension(WebApplicationBuilder builder)
    {
        /// <summary>Runs the MemoAna.Backend web application.</summary>
        /// <typeparam name="TProgram">The program type.</typeparam>
        /// <typeparam name="TApp">The root component.</typeparam>
        /// <returns>A task for application startup.</returns>
        public async Task RunMemoAnaAsync<TProgram, TApp>(Action<WebApplicationBuilder> configurePresentationServices)
            where TProgram : class
            where TApp : IComponent
        {

            _ = builder.Configuration
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables();

            if (builder.Environment.IsProduction())
            {

                var settings = new InfisicalSdkSettingsBuilder()    
                    //.WithHostUri("http://localhost:8080") // Optional. Will default to https://app.infisical.com
                    .Build();

                var client = new InfisicalClient(settings);

                string cid = Environment.GetEnvironmentVariable("MUACID")?.ToString() ?? throw new InvalidOperationException("Environment variables not set up");
                string cs = Environment.GetEnvironmentVariable("MUACS")?.ToString() ?? throw new InvalidOperationException("Environment variables not set up");

                MachineIdentityCredential credential = await client.Auth().UniversalAuth().LoginAsync(cid, cs);

                var options = new ListSecretsOptions
                {
                    SetSecretsAsEnvironmentVariables = true,
                    EnvironmentSlug = "prod",
                    SecretPath = "/memoana",
                    Recursive = true,
                    ExpandSecretReferences = true,
                    ProjectId = "",
                    ViewSecretValue = true,
                };

                Secret[] secrets = await client.Secrets().ListAsync(options) ?? throw new InvalidOperationException("Failed to fetch secrets, returned null response");
            }
            _ = builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();
            _ = builder.Services.AddControllers();
            _ = builder.Services.AddSignalR();
            _ = builder.Services.AddOpenApi();
            
            configurePresentationServices?.Invoke(builder);

            _ = builder.Services
                .AddHealthChecks()
                .AddCheck<ApiCheck>("ApiCheck")
                .AddCheck<ApiDiskUsageCheck>("ApiDiskUsageCheck")
                .AddCheck<HostInfoCheck>("HostInfoCheck")
                .AddCheck<DatabaseCheck>("DatabaseCheck");

            _ = builder.Services.Configure<JwtOptions>(
                builder.Configuration.GetSection(
                    JwtOptions.SectionName));
            _ = builder.Services.Configure<ConnectionStringsOptions>(
                builder.Configuration.GetSection(
                    ConnectionStringsOptions.SectionName));

            _ = builder.Services.AddDbContext<MemoAnaDbContext>(
                options =>
                {
                    if (builder.Environment.IsProduction())
                    {
                        ConnectionStringsOptions cs = builder.Configuration
                            .GetSection(ConnectionStringsOptions.SectionName)
                            .Get<ConnectionStringsOptions>()
                            ?? throw new InvalidOperationException(
                                "MemoAna ConnectionStrings configuration is missing.");

                        _ = options.UseNpgsql(cs.MemoAna, sql => sql.CommandTimeout(90));
                    }
                    else if (builder.Environment.IsDevelopment())
                    {
                        string? ConnectionStrings__Postgress = Environment.GetEnvironmentVariable("ConnectionStrings__Postgress")?.ToString();
                        ArgumentException.ThrowIfNullOrEmpty(ConnectionStrings__Postgress, nameof(ConnectionStrings__Postgress));
                        ArgumentException.ThrowIfNullOrWhiteSpace(ConnectionStrings__Postgress, nameof(ConnectionStrings__Postgress));
                        _ = options.UseNpgsql(ConnectionStrings__Postgress, sql => sql.CommandTimeout(90));
                    }
                    else throw new InvalidOperationException("No data provider configured");
                });

            _ = builder.Services.AddIdentityCore<User>(
                options =>
                {
                    options.User.RequireUniqueEmail = true;
                    options.SignIn.RequireConfirmedEmail = false;
                    options.Password.RequiredLength = 8;
                    options.Password.RequireDigit = true;
                    options.Password.RequireUppercase = true;
                    options.Password.RequireLowercase = true;
                    options.Password.RequireNonAlphanumeric = true;
                    options.Lockout.MaxFailedAccessAttempts = 5;
                    options.Lockout.DefaultLockoutTimeSpan =
                        TimeSpan.FromMinutes(15);
                })
                .AddRoles<Role>()
                .AddEntityFrameworkStores<MemoAnaDbContext>()
                .AddSignInManager()
                .AddDefaultTokenProviders();

            _ = builder.Services.AddScoped<IIdentityService, IdentityService>();

            _ = builder.Services.Configure<GooglePlayGamesOptions>(
                builder.Configuration.GetSection("GooglePlayGames"));
            _ = builder.Services.AddScoped<IGooglePlayGamesAuthenticationService, GooglePlayGamesAuthenticationService>();

            _ = builder.Services.AddSingleton<IRevokedTokenStore, RevokedTokenStore>();
            _ = builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
            _ = builder.Services.AddScoped<IIdentityEmailSender, LoggingIdentityEmailSender>();
            _ = builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
            _ = builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
            _ = builder.Services.AddValidatorsFromAssemblyContaining<RegisterCommandValidator>();
            _ = builder.Services.AddScoped<IHealthService, HealthService>();
            _ = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    JwtOptions jwt = builder.Configuration
                        .GetSection(JwtOptions.SectionName)
                        .Get<JwtOptions>()
                        ?? throw new InvalidOperationException(
                            "JWT configuration is missing.");

                    if (Encoding.UTF8.GetByteCount(jwt.Key) < 32)
                    {
                        throw new InvalidOperationException(
                            "Jwt:Key must contain 256 bits.");
                    }

                    options.TokenValidationParameters =
                        new TokenValidationParameters
                        {
                            ValidateIssuerSigningKey = true,
                            IssuerSigningKey =
                                new SymmetricSecurityKey(
                                    Encoding.UTF8.GetBytes(
                                        jwt.Key)),
                            ValidateIssuer = true,
                            ValidIssuer = jwt.Issuer,
                            ValidateAudience = true,
                            ValidAudience = jwt.Audience,
                            ValidateLifetime = true,
                            ClockSkew = TimeSpan.FromSeconds(30)
                        };

                    options.Events = new JwtBearerEvents
                    {
                        OnTokenValidated = context =>
                        {
                            JwtSecurityToken? token =
                                context.SecurityToken
                                    as JwtSecurityToken;
                            string? tokenType = token?
                                .Claims
                                .FirstOrDefault(
                                    claim => claim.Type ==
                                        JwtRegisteredClaimNames.Typ)
                                ?.Value;

                            if (!string.Equals(
                                tokenType,
                                "access",
                                StringComparison.Ordinal))
                            {
                                context.Fail(
                                    "The token is not an access token.");
                                return Task.CompletedTask;
                            }

                            if (token is not null &&
                                context.HttpContext
                                    .RequestServices
                                    .GetRequiredService<
                                        IRevokedTokenStore>()
                                    .IsRevoked(token.Id))
                            {
                                context.Fail(
                                    "The access token "
                                    + "has been revoked.");
                            }

                            return Task.CompletedTask;
                        }
                    };
                });

            _ = builder.Services.AddAuthorizationBuilder()
                .AddPolicy(IdentityPolicies.Administrator,
                    policy => policy.RequireClaim(
                        IdentityClaimTypes.Permission,
                        "system.admin"))
                .AddPolicy(IdentityPolicies.User,
                    policy => policy.RequireClaim(
                        IdentityClaimTypes.Permission,
                        "system.user"));

            _ = builder.Services.AddMediator(options =>
            {
                options.ServiceLifetime = ServiceLifetime.Scoped;
                options.Assemblies = [typeof(IdentityHandlers).Assembly];
                options.PipelineBehaviors =
                [
                    typeof(ValidationMiddleware<,>),
                    typeof(TransactionMiddleware<,>)
                ];
            });

            await builder.Build().RunMemoAnaAsync<TApp>();
        }
    }
}
