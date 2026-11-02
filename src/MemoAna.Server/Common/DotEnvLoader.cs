using System.Text;

namespace MemoAna.Server.Common;

/// <summary>
/// Loads environment variables from a .env file when present in development or local runs,
/// without overwriting pre-existing system/container environment variables.
/// </summary>
public static class DotEnvLoader
{
    /// <summary>
    /// Searches for a .env file in the current or ancestor directories and loads its entries into the process environment.
    /// </summary>
    public static void Load(string? directory = null)
    {
        string? current = directory ?? Directory.GetCurrentDirectory();

        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(current, ".env");
            if (File.Exists(candidate))
            {
                LoadFile(candidate);
                return;
            }

            DirectoryInfo? parent = Directory.GetParent(current);
            if (parent == null || parent.FullName == current)
            {
                break;
            }

            current = parent.FullName;
        }
    }

    /// <summary>
    /// Loads key-value pairs from a specific .env file. Existing environment variables take precedence.
    /// </summary>
    public static void LoadFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        foreach (string rawLine in File.ReadAllLines(filePath, Encoding.UTF8))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            int separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            string key = line[..separatorIndex].Trim();
            string value = line[(separatorIndex + 1)..].Trim();

            // Strip surrounding quotes if present
            if ((value.StartsWith('"') && value.EndsWith('"')) ||
                (value.StartsWith('\'') && value.EndsWith('\'')))
            {
                if (value.Length >= 2)
                {
                    value = value[1..^1];
                }
            }

            // Existing environment variables (e.g., from Docker container environment) take precedence
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    /// <summary>
    /// Maps flat environment variables (e.g. GOOGLE_PLAY_GAMES_WEB_CLIENT_ID) into hierarchical configuration keys.
    /// </summary>
    public static Dictionary<string, string?> GetMappedConfiguration()
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        void MapIfSet(string envVar, string configKey)
        {
            string? value = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrEmpty(value))
            {
                map[configKey] = value;
            }
        }

        MapIfSet("GOOGLE_PLAY_GAMES_APPLICATION_ID", "GooglePlayGames:ApplicationId");
        MapIfSet("GOOGLE_PLAY_GAMES_ANDROID_CLIENT_ID", "GooglePlayGames:AndroidClientId");
        MapIfSet("GOOGLE_PLAY_GAMES_WEB_CLIENT_ID", "GooglePlayGames:ClientId");
        MapIfSet("GOOGLE_PLAY_GAMES_WEB_CLIENT_SECRET", "GooglePlayGames:ClientSecret");
        MapIfSet("GOOGLE_PLAY_GAMES_AUTHORITY", "GooglePlayGames:Authority");
        MapIfSet("GOOGLE_PLAY_GAMES_AUDIENCE", "GooglePlayGames:Audience");
        MapIfSet("GOOGLE_PLAY_GAMES_VALID_AUDIENCE", "GooglePlayGames:ValidAudience");
        MapIfSet("GOOGLE_PLAY_GAMES_REDIRECT_URI", "GooglePlayGames:RedirectUri");
        MapIfSet("JWT_KEY", "Jwt:Key");
        MapIfSet("JWT_ISSUER", "Jwt:Issuer");
        MapIfSet("JWT_AUDIENCE", "Jwt:Audience");

        return map;
    }
}

