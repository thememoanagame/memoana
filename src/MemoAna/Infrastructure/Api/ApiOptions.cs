namespace MemoAna.Infrastructure.Api;

public sealed class ApiOptions
{
#if ANDROID
    public string BaseUrl { get; set; } = "http://10.0.2.2:5274/";
#else
    public string BaseUrl { get; set; } = "http://localhost:5274/";
#endif
}
