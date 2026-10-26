using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace MemoAna.UITests;

internal sealed record NativeTestConfiguration(
    string WindowsExecutable,
    string AndroidApk,
    string AndroidDeviceSerial,
    string AndroidPackage,
    string AppiumCommand,
    string AppiumUrl,
    string ArtifactDirectory)
{
    public static NativeTestConfiguration Load()
    {
        string root = FindRepositoryRoot();
        string artifactDirectory = Path.Combine(root, "artifacts", "ui-tests");
        Directory.CreateDirectory(artifactDirectory);

        return new(
            GetPath("MEMOANA_WINDOWS_EXECUTABLE",
                Path.Combine(root, "src", "MemoAna", "bin", "Debug",
                    "net10.0-windows10.0.19041.0", "win-x64", "MemoAna.exe")),
            GetPath("MEMOANA_ANDROID_APK",
                Path.Combine(root, "src", "MemoAna", "bin", "Debug",
                    "net10.0-android", "com.remotecodehub.memoana-Signed.apk")),
            Environment.GetEnvironmentVariable("MEMOANA_ANDROID_DEVICE")
                ?? string.Empty,
            Environment.GetEnvironmentVariable("MEMOANA_ANDROID_PACKAGE")
                ?? "com.remotecodehub.memoana",
            Environment.GetEnvironmentVariable("MEMOANA_APPIUM_COMMAND")
                ?? "appium.cmd",
            Environment.GetEnvironmentVariable("MEMOANA_APPIUM_URL")
                ?? "http://127.0.0.1:4723",
            artifactDirectory);
    }

    public void ValidateWindows()
    {
        if (!File.Exists(WindowsExecutable))
            throw new InvalidOperationException(
                $"Windows artifact was not found: '{WindowsExecutable}'. " +
                "Build MemoAna for net10.0-windows10.0.19041.0 or set " +
                "MEMOANA_WINDOWS_EXECUTABLE.");
    }

    public void ValidateAndroid()
    {
        if (!File.Exists(AndroidApk))
            throw new InvalidOperationException(
                $"Android APK was not found: '{AndroidApk}'. " +
                "Build MemoAna for net10.0-android or set MEMOANA_ANDROID_APK.");
    }

    private static string GetPath(string variable, string fallback) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value
            ? Path.GetFullPath(value)
            : fallback;

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "MemoAna.slnx")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new InvalidOperationException(
                "Could not locate the MemoAna repository root.");
    }
}

internal static class NativePrerequisites
{
    public static async Task<string> SelectAndroidDeviceAsync(
        string? requestedSerial,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new("adb", "devices")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start adb.");
        string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        string error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"adb devices failed: {error}");

        string[] devices = [.. output.Split(Environment.NewLine,
                StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Where(line => line.EndsWith("\tdevice", StringComparison.Ordinal))
            .Select(line => line[..line.IndexOf('\t')])];
        if (!string.IsNullOrWhiteSpace(requestedSerial))
        {
            if (!devices.Contains(requestedSerial, StringComparer.Ordinal))
                throw new InvalidOperationException(
                    $"Selected Android device '{requestedSerial}' is not authorized or connected.");
            return requestedSerial;
        }
        return devices.Length switch
        {
            1 => devices[0],
            0 => throw new InvalidOperationException(
                "No authorized Android device is connected."),
            _ => throw new InvalidOperationException(
                "Multiple Android devices are connected. Set MEMOANA_ANDROID_DEVICE explicitly.")
        };
    }
}
