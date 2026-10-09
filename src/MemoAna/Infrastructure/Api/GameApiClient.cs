using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MemoAna.Infrastructure.Api;

public sealed class GameApiClient(HttpClient httpClient, JsonSerializerOptions jsonOptions) : IGameApiClient
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly JsonSerializerOptions _jsonOptions = jsonOptions;

    public Task<IReadOnlyList<ThemeSummary>> ListThemesAsync(CancellationToken ct = default) => GetAsync<IReadOnlyList<ThemeSummary>>("api/game/themes", ct);
    public Task<IReadOnlyList<DifficultyOption>> ListDifficultiesAsync(CancellationToken ct = default) => GetAsync<IReadOnlyList<DifficultyOption>>("api/game/difficulties", ct);

    public async Task<CreateRoomResponse> CreateRoomAsync(CreateRoomRequest request, CancellationToken ct = default)
        => await SendAsync<CreateRoomResponse>(new HttpRequestMessage(HttpMethod.Post, "api/game/rooms") { Content = JsonContent.Create(request, options: _jsonOptions) }, ct);

    public Task<GameState> GetStateAsync(string roomId, string accessToken, CancellationToken ct = default)
        => GetAsync<GameState>($"api/game/rooms/{Uri.EscapeDataString(roomId)}", ct, accessToken);

    public Task<AssetManifest> GetManifestAsync(string roomId, string accessToken, CancellationToken ct = default)
        => GetAsync<AssetManifest>($"api/game/rooms/{Uri.EscapeDataString(roomId)}/assets", ct, accessToken);

    public async Task<byte[]> DownloadAssetAsync(string roomId, string accessToken, string assetToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(assetToken) || assetToken.Any(c => !char.IsLetterOrDigit(c) && c is not '-' and not '_'))
            throw new GameApiException("invalid_asset_reference", "A referência do asset é inválida.");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/game/rooms/{Uri.EscapeDataString(roomId)}/assets/{Uri.EscapeDataString(assetToken)}");
        request.Headers.Add("X-Player-Token", accessToken);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private Task<T> GetAsync<T>(string path, CancellationToken ct, string? token = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (!string.IsNullOrEmpty(token)) request.Headers.Add("X-Player-Token", token);
        return SendAsync<T>(request, ct);
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken ct)
    {
        using (request)
        {
            using var response = await _httpClient.SendAsync(request, ct);
            await EnsureSuccessAsync(response, ct);
            var value = await response.Content.ReadFromJsonAsync<T>(_jsonOptions, ct);
            return value ?? throw new GameApiException("empty_response", "A API retornou uma resposta vazia.");
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        GameError? error = null;
        try { error = await response.Content.ReadFromJsonAsync<GameError>(_jsonOptions, ct); } catch (JsonException) { }
        var message = error?.Message ?? response.ReasonPhrase ?? "Não foi possível comunicar com a API.";
        throw new GameApiException(error?.Code ?? MapCode(response.StatusCode), message, (int)response.StatusCode);
    }

    private static string MapCode(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.NotFound => "not_found",
        HttpStatusCode.BadRequest => "bad_request",
        _ => "api_error"
    };
}
