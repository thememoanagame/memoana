using FluentValidation;
using Infisical.Sdk;
using Infisical.Sdk.Model;
using MemoAna.Backend.Application.Common.Abstractions;
using MemoAna.Backend.Application.Common.Contracts;
using MemoAna.Backend.Application.Common.Pipeline.Validation;
using MemoAna.Backend.Application.Game.Abstractions;
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
using MemoAna.Backend.Infrastructure.Game;
using MemoAna.Backend.Infrastructure.Persistence;
using MemoAna.Backend.Infrastructure.Persistence.Middlewares;
using MemoAna.Backend.Infrastructure.Persistence.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using JwtRegisteredClaimNames = Microsoft.IdentityModel.JsonWebTokens.JwtRegisteredClaimNames;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Logging;

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
            _ = builder.Services.Configure<MongoDbOptions>(
                builder.Configuration.GetSection(
                    MongoDbOptions.SectionName));

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
            _ = builder.Services.AddScoped<IGameDataService, GameDataService>();

            _ = builder.Services.Configure<GooglePlayGamesOptions>(
                builder.Configuration.GetSection("GooglePlayGames"));
            _ = builder.Services.AddScoped<IGooglePlayGamesAuthenticationService, GooglePlayGamesAuthenticationService>();

            _ = builder.Services.AddSingleton<IRevokedTokenStore, RevokedTokenStore>();
            _ = builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
            _ = builder.Services.AddScoped<IIdentityEmailSender, LoggingIdentityEmailSender>();
            _ = builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
            _ = builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
            _ = builder.Services.AddSingleton<MemoAnaMongoDbContext>();
            _ = builder.Services.AddScoped(
                typeof(INoRepository<>),
                typeof(NoRepository<>));
            _ = builder.Services.AddValidatorsFromAssemblyContaining<RegisterCommandValidator>();
            _ = builder.Services.AddScoped<IHealthService, HealthService>();
            System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();
            Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler.DefaultInboundClaimTypeMap.Clear();
            _ = builder.Services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "BearerSelector";
                    options.DefaultChallengeScheme = "BearerSelector";
                })
                .AddPolicyScheme("BearerSelector", "Local or Google JWT", options =>
                {
                    options.ForwardDefaultSelector = context =>
                    {
                        string? authorization = context.Request.Headers.Authorization;
                        if (!string.IsNullOrEmpty(authorization) && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        {
                            string token = authorization["Bearer ".Length..].Trim();
                            var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();

                            if (handler.CanReadToken(token))
                            {
                                var jwtToken = handler.ReadJwtToken(token);
                                if (jwtToken.Issuer.Contains("accounts.google.com"))
                                {
                                    return "GoogleJwt";
                                }
                            }
                        }

                        return "LocalJwt";
                    };
                })
                .AddJwtBearer("LocalJwt", options =>
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
                        OnAuthenticationFailed = context =>
                        {
                            var logger = context.HttpContext.RequestServices
                                .GetRequiredService<ILoggerFactory>()
                                .CreateLogger("JwtBearerDebug");

                            logger.LogError(context.Exception, "Falha na Autenticação JWT: {Message}", context.Exception.Message);
                            return Task.CompletedTask;
                        },
                        OnTokenValidated = context =>
                        {
                            var principal = context.Principal;
                            if (principal == null)
                            {
                                context.Fail("Invalid token principal.");
                                return Task.CompletedTask;
                            }

                            string? tokenType = principal.FindFirst("typ")?.Value
                                            ?? principal.FindFirst("http://schemas.openxmlformats.org/claims/type")?.Value;

                            if (string.IsNullOrEmpty(tokenType) && context.SecurityToken != null)
                            {
                                if (context.SecurityToken is Microsoft.IdentityModel.JsonWebTokens.JsonWebToken jwt)
                                {
                                    tokenType = jwt.Typ;
                                }
                                else if (context.SecurityToken is System.IdentityModel.Tokens.Jwt.JwtSecurityToken jwtLegacy)
                                {
                                    tokenType = jwtLegacy.Header.Typ;
                                }
                            }

                            if (!string.IsNullOrEmpty(tokenType) &&
                                !string.Equals(tokenType, "access", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(tokenType, "JWT", StringComparison.OrdinalIgnoreCase))
                            {
                                context.Fail($"The token is not an access token. Type found: '{tokenType}'.");
                                return Task.CompletedTask;
                            }

                            string? tokenId = principal.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value
                                            ?? principal.FindFirst("jti")?.Value;

                            if (!string.IsNullOrEmpty(tokenId))
                            {
                                var revokedStore = context.HttpContext.RequestServices.GetRequiredService<IRevokedTokenStore>();
                                if (revokedStore.IsRevoked(tokenId))
                                {
                                    context.Fail("The access token has been revoked.");
                                    return Task.CompletedTask;
                                }
                            }

                            return Task.CompletedTask;
                        }
                    };
                })
                .AddJwtBearer("GoogleJwt", options =>
                {
                    options.Authority = "https://accounts.google.com";
                    options.Audience = $"{builder.Configuration["GooglePlayGames:ClientId"]}.apps.googleusercontent.com"; // ClientId da Credencial Web
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuers = ["accounts.google.com", "https://accounts.google.com"],
                        ValidateAudience = true,
                        ValidAudience = $"{builder.Configuration["GooglePlayGames:ClientId"]}.apps.googleusercontent.com",
                        ValidateIssuerSigningKey = true,
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.FromMinutes(5)
                    };
                });

            _ = builder.Services.AddAuthorizationBuilder()
                .AddPolicy(IdentityPolicies.Administrator, policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim(IdentityClaimTypes.Permission, "system.admin"))

                .AddPolicy(IdentityPolicies.User, policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim(IdentityClaimTypes.Permission, "system.user"))

                .SetDefaultPolicy(new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build());

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
