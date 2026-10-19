using MemoAna.Common.Abstract.Services;
using Microsoft.Extensions.Logging;

namespace MemoAna.Common.Services;

public sealed class ImageConverterService(HttpClient client) : IImageConverterService
{
    public async Task<string> ImageFromFileToBase64Async(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Arquivo de imagem não encontrado.", filePath);

        byte[] imageBytes = await File.ReadAllBytesAsync(filePath);
        return Convert.ToBase64String(imageBytes);
    }

    public async Task<string> ImageFromUrlToBase64Async(string url)
    {
        try
        {
            byte[] imageBytes = await client.GetByteArrayAsync(url);
            return Convert.ToBase64String(imageBytes);
        }
        catch 
        {
            throw;
        }
    }

    public byte[] Base64ToByteArray(string base64String)
    {
        if (string.IsNullOrWhiteSpace(base64String))
            return [];

        return Convert.FromBase64String(base64String.Trim());
    }

    
}