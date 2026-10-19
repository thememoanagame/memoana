# Testing Guidelines

Tests live under `tests/`. Extend those patterns instead of creating a separate test architecture.

## Required coverage for feature work

Use isolated test data in unit tests.

## MemoAna.UnitTests + DeviceRunners strategy

The `tests/MemoAna.UnitTests` project is a MAUI test application integrated with DeviceRunners. Unit tests must be implemented in this project and validated by executing the published application through the repository's `device-runners` .NET tool.

The canonical validation sequence is:

1. Write or update unit tests in `tests/MemoAna.UnitTests`.
2. From the repository root, publish `tests/MemoAna.UnitTests/MemoAna.UnitTests.csproj` using the `Release` configuration and `net10.0-windows10.0.19041.0` as the target framework to a temporary directory under the repository root, such as `tests/publish`.
3. Restore the repository-local .NET tools from the root `dotnet-tools.json` with `dotnet tool restore`.
4. Execute the published Windows MAUI test application with DeviceRunners:

   ```powershell
   dotnet device-runners windows test --app .\tests\publish\MemoAna.UnitTests.exe
   ```

5. Inspect the DeviceRunners result. Publishing alone does not constitute test execution or validation.

All commands in this workflow must be executed outside a sandbox/container environment. The MAUI test host and DeviceRunners runtime require the normal host environment, so agents must not substitute sandboxed execution for the canonical workflow.

The `tests/publish` directory is temporary generated output. This directory should not  be committed unless a task explicitly requires checked-in artifacts.

Do not replace the DeviceRunners execution with `dotnet test` when validating this test suite. If DeviceRunners cannot run because of an environment/infrastructure problem, preserve the exact command and diagnostics and distinguish that infrastructure failure from test or product failures.

## Native UI automation

`tests/MemoAna.UITests` owns the lifecycle of native UI sessions. Windows
sessions start the discovered `MemoAna.exe` and connect Playwright to a
WebView2 debugging port configured through the process environment; Android
sessions select an authorized ADB device, install the discovered APK, start a
local Appium server with UiAutomator2, and switch to the application's WebView
context. No pre-started application or `MEMOANA_NATIVE_CDP_ENDPOINT` is used.

The Windows and Android sessions are independent. PVP tests start both sessions
before configuring the host and client and compare the card collections after
the host starts the real LAN session. Set `MEMOANA_WINDOWS_EXECUTABLE`,
`MEMOANA_ANDROID_APK`, `MEMOANA_ANDROID_DEVICE`, or `MEMOANA_APPIUM_URL` only
when the default artifact paths or local Appium endpoint do not apply.

Run the local UI suite with:

```powershell
dotnet test .\tests\MemoAna.UITests\MemoAna.UITests.csproj -c Release
```

Native UI failures are infrastructure failures when the EXE/APK, authorized
device, Appium server, or WebView automation context is unavailable. The
exception identifies the failed preparation stage; these failures must not be
reported as passing UI or PVP coverage.

## Cancellation

Include cancellation tests where cancellation is part of the provider/application contract. A cancellation request should propagate rather than being converted into a generic authentication failure.
