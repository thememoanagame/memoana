using Microsoft.Maui.ApplicationModel;
using MemoAna.Common.Abstract.Services;
using MemoAna.Common.Enums;

namespace MemoAna.Common.Services;

#if ANDROID
public sealed class AccessLocalNetworkPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
    [
        ("android.permission.ACCESS_LOCAL_NETWORK", true)
    ];
}
#endif

/// <summary>
/// Default platform adapter for checking and requesting permissions across supported operating systems.
/// </summary>
public class DefaultPermissionPlatformAdapter(
    MauiPlatform? platform = null,
    int? androidApiLevel = null,
    Func<AppPermission, CancellationToken, Task<PermissionStatus>>? customCheckHandler = null,
    Func<AppPermission, CancellationToken, Task<PermissionStatus>>? customRequestHandler = null) : IPermissionPlatformAdapter
{

    public async Task<PermissionStatus> CheckStatusAsync(AppPermission permission, CancellationToken cancellationToken = default)
    {
        if (customCheckHandler != null)
        {
            return await customCheckHandler(permission, cancellationToken);
        }

        MauiPlatform platform = ResolvePlatform();

        return permission switch
        {
            AppPermission.LocalNetwork => await CheckLocalNetworkStatusAsync(platform, cancellationToken),
            _ => PermissionStatus.Unknown
        };
    }

    public async Task<PermissionStatus> RequestAsync(AppPermission permission, CancellationToken cancellationToken = default)
    {
        if (customRequestHandler != null)
        {
            return await customRequestHandler(permission, cancellationToken);
        }

        MauiPlatform platform = ResolvePlatform();

        return permission switch
        {
            AppPermission.LocalNetwork => await RequestLocalNetworkStatusAsync(platform, cancellationToken),
            _ => PermissionStatus.Unknown
        };
    }

    private Task<PermissionStatus> CheckLocalNetworkStatusAsync(MauiPlatform platform, CancellationToken cancellationToken)
    {
        switch (platform)
        {
            case MauiPlatform.Windows:
                // Windows unpackaged application: standard Win32 sockets do not require runtime OS authorization.
                return Task.FromResult(PermissionStatus.Granted);

            case MauiPlatform.MacOS:
                // Mac Catalyst: network access is governed by App Sandbox entitlements (client and server).
                return Task.FromResult(PermissionStatus.Granted);

            case MauiPlatform.IOS:
                // iOS: the native prompt is triggered by the OS on first local network transmission.
                // Guided by NSLocalNetworkUsageDescription in Info.plist and multicast entitlement.
                return Task.FromResult(PermissionStatus.Granted);

            case MauiPlatform.Android:
                int apiLevel = ResolveAndroidApiLevel();
                if (apiLevel < 37)
                {
                    // Android API <= 36: INTERNET and ACCESS_NETWORK_STATE in AndroidManifest grant LAN sockets.
                    // No runtime permission prompt exists for local network access.
                    return Task.FromResult(PermissionStatus.Granted);
                }

#if ANDROID
                return Permissions.CheckStatusAsync<AccessLocalNetworkPermission>();
#else
                return Task.FromResult(PermissionStatus.Denied);
#endif

            default:
                return Task.FromResult(PermissionStatus.Granted);
        }
    }

    private Task<PermissionStatus> RequestLocalNetworkStatusAsync(MauiPlatform platform, CancellationToken cancellationToken)
    {
        switch (platform)
        {
            case MauiPlatform.Windows:
                // Windows unpackaged: automatically granted.
                return Task.FromResult(PermissionStatus.Granted);

            case MauiPlatform.MacOS:
                // Mac Catalyst: automatically granted by App Sandbox entitlements.
                return Task.FromResult(PermissionStatus.Granted);

            case MauiPlatform.IOS:
                // iOS: prompt is triggered on network transmission.
                return Task.FromResult(PermissionStatus.Granted);

            case MauiPlatform.Android:
                int apiLevel = ResolveAndroidApiLevel();
                if (apiLevel < 37)
                {
                    return Task.FromResult(PermissionStatus.Granted);
                }

#if ANDROID
                return Permissions.RequestAsync<AccessLocalNetworkPermission>();
#else
                return Task.FromResult(PermissionStatus.Denied);
#endif

            default:
                return Task.FromResult(PermissionStatus.Granted);
        }
    }

    private MauiPlatform ResolvePlatform()
    {
        if (platform.HasValue)
        {
            return platform.Value;
        }

#if WINDOWS
        return MauiPlatform.Windows;
#elif ANDROID
        return MauiPlatform.Android;
#elif IOS
        return MauiPlatform.IOS;
#elif MACCATALYST
        return MauiPlatform.MacOS;
#else
        if (OperatingSystem.IsWindows()) return MauiPlatform.Windows;
        if (OperatingSystem.IsAndroid()) return MauiPlatform.Android;
        if (OperatingSystem.IsIOS()) return MauiPlatform.IOS;
        if (OperatingSystem.IsMacCatalyst() || OperatingSystem.IsMacOS()) return MauiPlatform.MacOS;
        return MauiPlatform.Windows;
#endif
    }

    private int ResolveAndroidApiLevel()
    {
        if (androidApiLevel.HasValue)
        {
            return androidApiLevel.Value;
        }

#if ANDROID
        return (int)Android.OS.Build.VERSION.SdkInt;
#else
        return 0;
#endif
    }
}

