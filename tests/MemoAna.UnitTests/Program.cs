using DeviceRunners.VisualRunners;
using DeviceRunners.VisualRunners.Blazor.Components;

using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<TestRunnerApp>("#app");

builder.UseVisualTestRunner(conf => conf
    .AddXunit3()
    .AddTestAssembly(typeof(MemoAna.UnitTests.Game.GameServiceTests).Assembly)
    .AddConsoleResultChannel());

await builder.Build().RunAsync();