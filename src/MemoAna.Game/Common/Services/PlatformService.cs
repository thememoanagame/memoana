using MemoAna.Common.Abstract.Services;
using MemoAna.Common.Enums;

namespace MemoAna.Common.Services;

public sealed class PlatformService(MauiPlatform platform) : IPlatformService
{
    private readonly MauiPlatform _platform = platform;

    public bool IsWindows => _platform == MauiPlatform.Windows;

    public bool IsMacOS => _platform == MauiPlatform.MacOS;

    public bool IsAndroid => _platform == MauiPlatform.Android;

    public bool IsIOS => _platform == MauiPlatform.IOS;
}
