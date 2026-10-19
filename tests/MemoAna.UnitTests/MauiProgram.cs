using Microsoft.Extensions.Logging;
using DeviceRunners;
using DeviceRunners.VisualRunners;
using DeviceRunners.UITesting;
namespace MemoAna.UnitTests;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder.ConfigureUITesting().UseVisualTestRunner(conf => conf
		.AddCliConfiguration()
#if EXCLUDE_FAILING_TESTS
		// Exclude the intentionally-failing demo tests on CI device runs, which
		// auto-start without a runtime filter. Enabled only when built with
		// -p:IncludeFailingTests=false (the TCP and XHarness CI runs); local and
		// interactive runs leave it undefined so the failure demos stay visible.
		.SetTestCaseFilter("Category!=ExpectedFailure")
#endif 
		.EnableAutoStart(true)
		.AddTcpResultChannel(new TcpResultChannelOptions
		{
			HostNames = ["localhost", "10.0.2.2"],
			Port = 16384,
			Formatter = new EventStreamFormatter(),
			Required = false,
			Retries = 3,
			RetryTimeout = TimeSpan.FromSeconds(5),
			Timeout = TimeSpan.FromSeconds(30)
		}) 
		.AddConsoleResultChannel()
		.AddXunit3()
		.AddTestAssembly(typeof(MemoAna.UnitTests.Game.GameServiceTests).Assembly)
		.AddConsoleResultChannel());
#if DEBUG
		builder.Logging.AddDebug();
#else
		builder.Logging.AddConsole();
#endif

		return builder.Build();
	}
}
