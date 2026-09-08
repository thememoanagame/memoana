using MemoAna.Backend.Application.Common.Abstractions;
using MemoAna.Backend.Application.Common.Pipeline.Validation;
using MemoAna.Backend.Application.Identity.Abstractions;
using MemoAna.Backend.Application.Identity.Handlers;
using MemoAna.Backend.Infrastructure.Common.UnitOfWork;
using MemoAna.Backend.Infrastructure.Identity.Models;
using MemoAna.Backend.Infrastructure.Identity.Options;
using MemoAna.Backend.Infrastructure.Identity.Services;
using MemoAna.Backend.Infrastructure.Persistence;
using MemoAna.Backend.Infrastructure.Persistence.Middlewares;
using MemoAna.Backend.UnitTests.Common.Mocks;
using Mediator;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MemoAna.Backend.UnitTests.Common.Fixtures;

/// <summary>Builds an in-memory application service provider for integration-style unit tests.</summary>
public sealed class MemoAnaBackendTestFixture : IDisposable
{
    private readonly ServiceProvider _provider;

    /// <summary>Initializes the test fixture and its dependency injection container.</summary>
    public MemoAnaBackendTestFixture()
    {
        ServiceCollection services = new();
        _ = services.AddLogging();
        _ = services.AddHttpContextAccessor();
        _ = services.AddSignalR();
        _ = services.AddDbContext<MemoAnaDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        _ = services.AddIdentityCore<User>(options =>
        {
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredLength = 4;
            options.SignIn.RequireConfirmedEmail = false;
        }).AddRoles<Role>()
        .AddEntityFrameworkStores<MemoAnaDbContext>()
        .AddSignInManager();
        _ = services.AddScoped<IIdentityEmailSender,
            TestIdentityEmailSender>();
        _ = services.Configure<JwtOptions>(options =>
        {
            options.Key = "01234567890123456789012345678901";
            options.Issuer = "MemoAna.Backend.Tests";
            options.Audience = "MemoAna.Backend.Tests";
        });
        _ = services.AddSingleton<IRevokedTokenStore, RevokedTokenStore>();
        _ = services.AddScoped<IJwtTokenService, JwtTokenService>();
        _ = services.AddScoped<IdentityService>();
        _ = services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Register the validators consumed by ValidationMiddleware, matching the
        // application composition root. Without this registration the middleware
        // receives an empty validator collection and invalid commands reach handlers.
        //services.AddScoped<IValidator<SetRelayCommand>, SetRelayCommandValidator>();

        // Dispatch tests intentionally use a deterministic fake at the application boundary.
        // The concrete SignalR implementation is also registered so it can be resolved and tested
        // independently with the real IHubContext supplied by AddSignalR().
        //services.AddScoped<FakeSignalRService>();
        //services.AddScoped<ISignalRService>(sp =>
        //sp.GetRequiredService<FakeSignalRService>());
        //services.AddScoped<SignalRService>();

        _ = services.AddScoped(typeof(IRepository<>), typeof(FakeRepository<>));
        //services.AddScoped(sp => new SignalRHandlers(sp.GetRequiredService<ISignalRService>()));
        _ = services.AddMediator(options =>
        {
            options.ServiceLifetime = ServiceLifetime.Scoped;
            options.Assemblies = [typeof(IdentityHandlers).Assembly];
            options.PipelineBehaviors =
            [
                typeof(ValidationMiddleware<,>),
                typeof(TransactionMiddleware<,>)
            ];
        });

        _provider = services.BuildServiceProvider();
    }

    /// <summary>Gets the service provider used by the fixture.</summary>
    public ServiceProvider Services => _provider;

    /// <summary>Gets the IMediator service under test.</summary>
    public IMediator Mediator => _provider.GetRequiredService<IMediator>();

    /// <summary>Gets the UnitOfWork service under test.</summary>
    public IUnitOfWork UnitOfWork => _provider.GetRequiredService<IUnitOfWork>();

    /// <summary>Gets the fake SignalR boundary used by dispatch tests.</summary>
    // public FakeSignalRService FSignalR =>
    //     _provider.GetRequiredService<FakeSignalRService>();

    /// <summary>Gets the concrete SignalR service used by implementation tests.</summary>
    //public SignalRService SignalR => _provider.GetRequiredService<SignalRService>();

    /// <summary>Gets the Identity service under test.</summary>
    public IdentityService Identity => _provider.GetRequiredService<IdentityService>();

    /// <summary>Gets the database context.</summary>
    public MemoAnaDbContext DbContext => _provider.GetRequiredService<MemoAnaDbContext>();

    /// <summary>Gets the user manager.</summary>
    public UserManager<User> UserManager =>
        _provider.GetRequiredService<UserManager<User>>();

    /// <summary>Gets the role manager.</summary>
    public RoleManager<Role> RoleManager =>
        _provider.GetRequiredService<RoleManager<Role>>();

    /// <summary>Creates and stores a user.</summary>
    /// <param name="email">The user's email.</param>
    /// <param name="password">The user's password.</param>
    /// <returns>The created user.</returns>
    public async Task<User> CreateUserAsync(
        string email,
        string password = "Password1!")
    {
        User user = new(email)
        {
            Email = email,
            EmailConfirmed = true
        };
        IdentityResult result = await UserManager.CreateAsync(user, password);
        Assert.True(result.Succeeded);
        return user;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _provider.Dispose();
    }

    private sealed class TestIdentityEmailSender : IIdentityEmailSender
    {
        public Task SendConfirmationAsync(
            string email,
            string confirmationLink,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SendPasswordResetAsync(
            string email,
            string resetLink,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
