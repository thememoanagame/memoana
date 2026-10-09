using System.Security.Cryptography;

namespace MemoAna.Infrastructure.Assets;

public interface IGameAssetStore
{
    Task<string> EnsureAssetAsync(string roomId, string token, string contentType, Func<Task<byte[]>> download, CancellationToken cancellationToken = default);
}

public sealed class LocalGameAssetStore : IGameAssetStore
{
    public async Task<string> EnsureAssetAsync(string roomId, string token, string contentType, Func<Task<byte[]>> download, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(token) || token.Any(c => !char.IsLetterOrDigit(c) && c is not '-' and not '_'))
            throw new InvalidOperationException("A referência do asset é inválida.");
        var roomPath = Path.Combine(FileSystem.AppDataDirectory, "assets", Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(roomId))).ToLowerInvariant());
        Directory.CreateDirectory(roomPath);
        var filePath = Path.Combine(roomPath, Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token))).ToLowerInvariant() + ".bin");
        if (!File.Exists(filePath)) await File.WriteAllBytesAsync(filePath, await download(), ct);
        var bytes = await File.ReadAllBytesAsync(filePath, ct);
        return $"data:{(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType)};base64,{Convert.ToBase64String(bytes)}";
    }
}
