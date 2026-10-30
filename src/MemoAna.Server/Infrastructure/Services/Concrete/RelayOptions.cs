namespace MemoAna.Server.Infrastructure.Services.Concrete;

public class RelayOptions
{
    public const string SectionName = "Relay";

    public int Port { get; set; } = 35680;
    public int InactivityTimeoutMinutes { get; set; } = 15;
    public int UnconnectedRoomTimeoutMinutes { get; set; } = 5;
    public int CleanupIntervalSeconds { get; set; } = 30;
}

