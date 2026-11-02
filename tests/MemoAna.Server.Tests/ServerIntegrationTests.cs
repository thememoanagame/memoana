using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using MemoAna.Auth.Abstract;
using MemoAna.Auth.Dtos;
using MemoAna.Auth.Responses;
using MemoAna.Common.Base;
using MemoAna.Game.Models;
using MemoAna.Server.Application.Relay.Responses;
using MemoAna.Server.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MemoAna.Server.Tests;

public class TestGooglePlayGamesClient : IGooglePlayGamesClient
{
    public GoogleTokenExchangeResult? TokenResult { get; set; } =
        new("mock-google-access-token", "mock-google-id-token", "mock-google-refresh-token", 3600);

    public GooglePlayerProfileResult? ProfileResult { get; set; } =
        new("g_player_mock_123", "MockPlayGamer", "https://img.mock/gamer.png", "mockplayer@gmail.com", true, "Gamer", "Test");

    public Task<GoogleTokenExchangeResult?> ExchangeCodeAsync(string serverAuthCode, string redirectUri, CancellationToken cancellationToken)
    {
        if (serverAuthCode == "invalid-code")
        {
            return Task.FromResult<GoogleTokenExchangeResult?>(null);
        }
        return Task.FromResult(TokenResult);
    }

    public Task<GooglePlayerProfileResult?> GetPlayerProfileAsync(string accessToken, string? idToken, CancellationToken cancellationToken)
    {
        return Task.FromResult(ProfileResult);
    }
}

public class ServerWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"test_server_{Guid.NewGuid():N}.db";
    public TestGooglePlayGamesClient MockGoogleClient { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var dbContextDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ServerDbContext>));
            if (dbContextDescriptor != null)
            {
                services.Remove(dbContextDescriptor);
            }

            services.AddDbContext<ServerDbContext>(options =>
            {
                options.UseSqlite($"Data Source={_databaseName};Cache=Shared;Foreign Keys=True;");
            });

            var googleClientDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IGooglePlayGamesClient));
            if (googleClientDescriptor != null)
            {
                services.Remove(googleClientDescriptor);
            }
            services.AddSingleton<IGooglePlayGamesClient>(MockGoogleClient);
        });

        builder.UseEnvironment("Testing");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (File.Exists(_databaseName))
        {
            try { File.Delete(_databaseName); } catch { }
        }
    }
}

public class ServerIntegrationTests : IClassFixture<ServerWebApplicationFactory>
{
    private readonly ServerWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ServerIntegrationTests(ServerWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HealthCheck_Returns200AndHealthyStatus()
    {
        var response = await _client.GetAsync("/api/v1/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Healthy", content);
    }

    [Fact]
    public async Task OpenApi_Returns200AndValidJson()
    {
        var response = await _client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("openapi", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TraditionalAuth_RegisterAndLogin_ReturnsValidTokens()
    {
        string email = $"user_{Guid.NewGuid():N}@example.com";
        var registerDto = new RegisterDto(email, "StrongP@ss123!");

        var regResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", registerDto);
        Assert.Equal(HttpStatusCode.OK, regResponse.StatusCode);

        var loginDto = new LoginDto(email, "StrongP@ss123!");
        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", loginDto);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var tokens = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(tokens);
        Assert.Equal("Bearer", tokens.TokenType);
        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));
    }

    [Fact]
    public async Task TraditionalLogin_InvalidPassword_Returns401Unauthorized()
    {
        string email = $"user_{Guid.NewGuid():N}@example.com";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterDto(email, "StrongP@ss123!"));

        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginDto(email, "WrongPass123!"));

        Assert.Equal(HttpStatusCode.Unauthorized, loginResponse.StatusCode);
    }

    [Fact]
    public async Task GooglePlayGamesLogin_WithSimulatedClient_CreatesUserAndReturnsTokens()
    {
        var gpgDto = new GooglePlayGamesLoginDto("simulated-server-auth-code", "");

        var response = await _client.PostAsJsonAsync("/api/v1/auth/login/google-play-games", gpgDto);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(tokens);
        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));
    }

    [Fact]
    public async Task GooglePlayGamesLogin_InvalidCode_Returns401Unauthorized()
    {
        var gpgDto = new GooglePlayGamesLoginDto("invalid-code", "");

        var response = await _client.PostAsJsonAsync("/api/v1/auth/login/google-play-games", gpgDto);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetInfo_AuthenticatedUser_ReturnsProfileData()
    {
        string playerId = $"g_player_profile_{Guid.NewGuid():N}";
        _factory.MockGoogleClient.ProfileResult = new GooglePlayerProfileResult(
            playerId, "MockPlayGamer", "https://img.mock/gamer.png", "mockplayer@gmail.com", true, "Gamer", "Test");

        var gpgDto = new GooglePlayGamesLoginDto("simulated-auth-code-profile", "");
        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login/google-play-games", gpgDto);
        var tokens = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(tokens);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/manage/info");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var info = await response.Content.ReadFromJsonAsync<IdentityInfoResponse>();
        Assert.NotNull(info);
        Assert.Equal("MockPlayGamer", info.DisplayName);
        Assert.Equal(playerId, info.PlayerId);
    }

    [Fact]
    public async Task GetInfo_Unauthenticated_Returns401Unauthorized()
    {
        var response = await _client.GetAsync("/api/v1/auth/manage/info");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LinkGooglePlayGames_TraditionalUser_LinksSuccessfullyAndAllowsSubsequentGpgLogin()
    {
        string email = $"linkuser_{Guid.NewGuid():N}@example.com";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterDto(email, "StrongP@ss123!"));
        var loginRes = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginDto(email, "StrongP@ss123!"));
        var tokens = await loginRes.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(tokens);

        string uniquePlayerId = $"gpg_unique_{Guid.NewGuid():N}";
        _factory.MockGoogleClient.ProfileResult = new GooglePlayerProfileResult(uniquePlayerId, "LinkedPlayerTag");

        using var linkRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/manage/link/google-play-games");
        linkRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        linkRequest.Content = JsonContent.Create(new GooglePlayGamesLoginDto("auth-code-for-linking", ""));

        var linkResponse = await _client.SendAsync(linkRequest);
        Assert.Equal(HttpStatusCode.OK, linkResponse.StatusCode);

        // Subsequent Google Play Games login with the same player key resolves to the same user
        var gpgLoginRes = await _client.PostAsJsonAsync("/api/v1/auth/login/google-play-games", new GooglePlayGamesLoginDto("subsequent-code", ""));
        Assert.Equal(HttpStatusCode.OK, gpgLoginRes.StatusCode);

        var gpgTokens = await gpgLoginRes.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(gpgTokens);

        using var profileRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/manage/info");
        profileRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", gpgTokens.AccessToken);
        var profileRes = await _client.SendAsync(profileRequest);
        var profile = await profileRes.Content.ReadFromJsonAsync<IdentityInfoResponse>();
        Assert.NotNull(profile);
        Assert.Equal(email, profile.Email);
        Assert.Equal(uniquePlayerId, profile.PlayerId);
    }

    [Fact]
    public async Task RefreshToken_ValidToken_ReturnsNewTokensAndRejectsReplay()
    {
        string email = $"refresh_{Guid.NewGuid():N}@example.com";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterDto(email, "StrongP@ss123!"));
        var loginRes = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginDto(email, "StrongP@ss123!"));
        var tokens = await loginRes.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(tokens);

        var refreshRes = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshDto(tokens.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, refreshRes.StatusCode);
        var newTokens = await refreshRes.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(newTokens);
        Assert.NotEqual(tokens.AccessToken, newTokens.AccessToken);

        // Replay of old refresh token must fail with 401
        var replayRes = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshDto(tokens.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, replayRes.StatusCode);
    }

    [Fact]
    public async Task RevokeToken_ValidAccessToken_RevokesImmediately()
    {
        string email = $"revoke_{Guid.NewGuid():N}@example.com";
        await _client.PostAsJsonAsync("/api/v1/auth/register", new RegisterDto(email, "StrongP@ss123!"));
        var loginRes = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginDto(email, "StrongP@ss123!"));
        var tokens = await loginRes.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(tokens);

        using var revokeReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/revoke");
        revokeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var revokeRes = await _client.SendAsync(revokeReq);
        Assert.Equal(HttpStatusCode.OK, revokeRes.StatusCode);

        // Accessing protected endpoint with revoked access token must now return 401
        using var protectedReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/manage/info");
        protectedReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var protectedRes = await _client.SendAsync(protectedReq);
        Assert.Equal(HttpStatusCode.Unauthorized, protectedRes.StatusCode);
    }

    [Fact]
    public async Task Relay_CreateRoomAndListRooms_UnauthenticatedOperationsSucceed()
    {
        string roomName = $"TestRoom_{Guid.NewGuid():N}";
        var createDto = new CreateRoomDto("host_player_1", roomName, null, "Space", 2, "Astronaut");

        var createRes = await _client.PostAsJsonAsync("/api/v1/relay/rooms", createDto);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);

        var createdRoom = await createRes.Content.ReadFromJsonAsync<CreateRoomResponse>();
        Assert.NotNull(createdRoom);
        Assert.Equal(roomName, createdRoom.RoomName);
        Assert.False(string.IsNullOrWhiteSpace(createdRoom.RoomId));
        Assert.True(createdRoom.RelayPort > 0);
        Assert.False(string.IsNullOrWhiteSpace(createdRoom.ConnectionToken));

        // List rooms and verify created room is present
        var listRes = await _client.GetAsync("/api/v1/relay/rooms");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var rooms = await listRes.Content.ReadFromJsonAsync<List<RelayRoomSummary>>();
        Assert.NotNull(rooms);
        Assert.Contains(rooms, r => r.RoomId == createdRoom.RoomId);

        // Join room
        var joinDto = new JoinRoomDto("guest_player_2", null, "Cosmonaut");
        var joinRes = await _client.PostAsJsonAsync($"/api/v1/relay/rooms/{createdRoom.RoomId}/join", joinDto);
        Assert.Equal(HttpStatusCode.OK, joinRes.StatusCode);
        var joined = await joinRes.Content.ReadFromJsonAsync<JoinRoomResponse>();
        Assert.NotNull(joined);
        Assert.Equal(createdRoom.RoomId, joined.RoomId);

        // Leave room
        var leaveDto = new LeaveRoomDto("guest_player_2");
        var leaveRes = await _client.PostAsJsonAsync($"/api/v1/relay/rooms/{createdRoom.RoomId}/leave", leaveDto);
        Assert.Equal(HttpStatusCode.NoContent, leaveRes.StatusCode);

        // Close room
        var closeRes = await _client.DeleteAsync($"/api/v1/relay/rooms/{createdRoom.RoomId}");
        Assert.Equal(HttpStatusCode.NoContent, closeRes.StatusCode);
    }

    [Fact]
    public async Task EndToEnd_IdentityToRelay_FullJourneyWorkflow()
    {
        // 1. Authenticate with Google Play Games
        _factory.MockGoogleClient.ProfileResult = new GooglePlayerProfileResult(
            $"e2e_player_{Guid.NewGuid():N}",
            "E2EPlayer",
            "https://avatar.test/e2e.png",
            "e2e@example.com",
            true);

        var authRes = await _client.PostAsJsonAsync(
            "/api/v1/auth/login/google-play-games",
            new GooglePlayGamesLoginDto("e2e-auth-code", ""));
        Assert.Equal(HttpStatusCode.OK, authRes.StatusCode);
        var tokens = await authRes.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(tokens);

        // 2. Fetch authenticated identity information
        using var profileReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/manage/info");
        profileReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var profileRes = await _client.SendAsync(profileReq);
        Assert.Equal(HttpStatusCode.OK, profileRes.StatusCode);
        var profile = await profileRes.Content.ReadFromJsonAsync<IdentityInfoResponse>();
        Assert.NotNull(profile);
        Assert.Equal("E2EPlayer", profile.DisplayName);

        // 3. Refresh token
        var refreshRes = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshDto(tokens.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, refreshRes.StatusCode);
        var refreshedTokens = await refreshRes.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(refreshedTokens);

        // 4. Create room on Relay using PlayerId
        var createRoomDto = new CreateRoomDto(
            profile.PlayerId!,
            "E2E Tournament Room",
            null,
            "Classic",
            1,
            profile.DisplayName);

        var createRoomRes = await _client.PostAsJsonAsync("/api/v1/relay/rooms", createRoomDto);
        Assert.Equal(HttpStatusCode.Created, createRoomRes.StatusCode);
        var room = await createRoomRes.Content.ReadFromJsonAsync<CreateRoomResponse>();
        Assert.NotNull(room);

        // 5. Query room list to verify discoverability
        var listRes = await _client.GetAsync("/api/v1/relay/rooms");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var list = await listRes.Content.ReadFromJsonAsync<List<RelayRoomSummary>>();
        Assert.NotNull(list);
        Assert.Contains(list, r => r.RoomId == room.RoomId);

        // 6. Revoke access token
        using var revokeReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/revoke");
        revokeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshedTokens.AccessToken);
        var revokeRes = await _client.SendAsync(revokeReq);
        Assert.Equal(HttpStatusCode.OK, revokeRes.StatusCode);
    }
}
