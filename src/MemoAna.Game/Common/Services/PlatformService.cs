using MemoAna.Common.Abstract.Services;
using MemoAna.Common.Enums;

namespace MemoAna.Common.Services;

public sealed class PlatformService : IPlatformService
{
#if ANDROID
    private readonly MauiPlatform? _platform = MauiPlatform.Android;
#elif IOS
    private readonly MauiPlatform? _platform =MauiPlatform.IOS;
#elif MACCATALYST
    private readonly MauiPlatform? _platform = MauiPlatform.MacOS;
#elif WINDOWS
    private readonly MauiPlatform? _platform = MauiPlatform.Windows;
#else
    private readonly MauiPlatform? _platform = null;
#endif
    public bool IsWindows => _platform is not null && _platform == MauiPlatform.Windows;

    public bool IsMacOS => _platform is not null && _platform == MauiPlatform.MacOS;

    public bool IsAndroid => _platform is not null && _platform == MauiPlatform.Android;

    public bool IsIOS => _platform is not null && _platform == MauiPlatform.IOS;

    public string AppDataPath => DeviceInfo.Platform == DevicePlatform.Android || DeviceInfo.Platform == DevicePlatform.iOS ? FileSystem.AppDataDirectory : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".memoana");
}
