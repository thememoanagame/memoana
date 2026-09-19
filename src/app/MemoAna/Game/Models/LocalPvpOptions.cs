namespace MemoAna.Game.Models;

/// <summary>
/// Configures the single UDP port used by Local PVP discovery and sessions.
/// </summary>
public sealed class LocalPvpOptions
{
    /// <summary>
    /// Gets or sets the LAN port used by LiteNetLib.
    /// </summary>
    public int Port { get; set; } = 45874;

    /// <summary>
    /// Gets or sets the interval between discovery broadcasts.
    /// </summary>
    public TimeSpan DiscoveryInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the connection handshake timeout.
    /// </summary>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
