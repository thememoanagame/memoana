using MemoAna.Common.Abstract.Services;
using MemoAna.Common.Enums;

namespace MemoAna.Common.Services;

public sealed class PlatformService(MauiPlatform platform) : IPlatformService
{
    public MauiPlatform Platform { get; } = platform;

    public bool IsWindows => platform == MauiPlatform.Windows;

    public bool IsMacOS => platform == MauiPlatform.MacOS;

    public bool IsAndroid => platform == MauiPlatform.Android;

    public bool IsIOS => platform == MauiPlatform.IOS;
}
