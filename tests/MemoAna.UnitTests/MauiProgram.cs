using Microsoft.Extensions.Logging;
using DeviceRunners.UITesting;
using DeviceRunners.VisualRunners;
using DeviceRunners;
namespace MemoAna.UnitTests;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.ConfigureUITesting()
			.UseVisualTestRunner(conf => conf
				.AddCliConfiguration()     // Reads config from DeviceRunners CLI or dotnet test
				.AddConsoleResultChannel()
				.AddTcpResultChannel(new TcpResultChannelOptions()
				{
					HostName = "127.0.0.1",
					Port = 9090,
					Timeout = TimeSpan.FromSeconds(150)
				})
				.AddTestAssembly(typeof(MauiProgram).Assembly)
				.AddXunit3());
#if DEBUG
		builder.Logging.AddDebug();
#else
		builder.Logging.AddConsole();
#endif
		return builder.Build();
	}
}
