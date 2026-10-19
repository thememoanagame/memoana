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

## Cancellation

Include cancellation tests where cancellation is part of the provider/application contract. A cancellation request should propagate rather than being converted into a generic authentication failure.
