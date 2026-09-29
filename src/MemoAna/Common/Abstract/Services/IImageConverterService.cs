namespace MemoAna.Common.Abstract.Services;

public interface IImageConverterService
{
    Task<string> ImageFromFileToBase64Async(string filePath);
    Task<string> ImageFromUrlToBase64Async(string url);
    byte[] Base64ToByteArray(string base64String); 
}