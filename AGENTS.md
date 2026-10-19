# Agent Instructions

## Scope

These instructions apply to the entire MemoAna repository. Agents must read and follow the repository documentation, custom agents, and skills relevant to the task before modifying code.

## Repository guidance

Read the applicable documentation before implementation:

- [Agent Guidelines](docs/agent-guidelines.md)
- [.NET 10 Guidelines](docs/dotnet10-guidelines.md)
- [PvP Architecture and Behavior](docs/pvp.md)
- [Testing Guidelines](docs/testing-guidelines.md)

## Custom agents

The repository provides the following custom agent:

- [expert-reviewer](.github/agents/expert-reviewer.agent.md) — adversarial .NET MAUI code review guidance.

## Custom skills

Use the applicable repository skills when working on their corresponding areas:

- [maui-accessibility](.github/skills/maui-accessibility/SKILL.md)
- [maui-app-architecture](.github/skills/maui-app-architecture/SKILL.md)
- [maui-current-apis](.github/skills/maui-current-apis/SKILL.md)
- [maui-devflow-debug](.github/skills/maui-devflow-debug/SKILL.md)
- [maui-devflow-onboard](.github/skills/maui-devflow-onboard/SKILL.md)
- [maui-devflow-session-review](.github/skills/maui-devflow-session-review/SKILL.md)
- [maui-project-structure](.github/skills/maui-project-structure/SKILL.md)
- [maui-ui-patterns](.github/skills/maui-ui-patterns/SKILL.md)
- [maui-unit-testing](.github/skills/maui-unit-testing/SKILL.md)

## Testing strategy

For unit-test work in this repository, use the `MemoAna.UnitTests` MAUI test
application with DeviceRunners. The test project is the executable test
application that is published and then executed by the DeviceRunners CLI.

### Required test workflow

All commands in this workflow must be executed **outside any sandboxed environment**. Do not use a sandbox/container abstraction for `dotnet publish`, tool restoration, or DeviceRunners execution, because the test host and browser/device runtime require the normal host environment.

From the repository root:

1. Implement or update unit tests in `tests/MemoAna.UnitTests`.
2. Publish the test project in `Release` configuration to a temporary directory under the repository root, for example:

   ```powershell
   dotnet publish .\tests\MemoAna.UnitTests\MemoAna.UnitTests.csproj -c Release -o .\tests\publish
   ```

3. Restore the repository-local .NET tools defined in [`dotnet-tools.json`](dotnet-tools.json):

   ```powershell
   dotnet tool restore
   ```

4. Execute the published Windows MAUI tests with `device-runners`:

   ```powershell
   dotnet device-runners windows test --app .\tests\publish\MemoAna.UnitTests.exe
   ```

5. Inspect the generated TRX and command output. A test task is not complete merely because the project publishes successfully; the DeviceRunners execution must be performed and its result evaluated.

### Test-output hygiene

- `tests/publish` is a temporary publish directory and must not be treated as source code.
- `artifacts` contains generated test logs and must not be committed unless a task explicitly requires checked-in results.
- Do not change the test execution strategy to `dotnet test` when the task requires validation through DeviceRunners.
- If DeviceRunners fails because of an environment or infrastructure problem, report the failure separately from product/test failures and preserve the exact command and relevant diagnostics.

## General agent behavior

- Inspect the existing implementation and tests before changing architecture or introducing new test infrastructure.
- Prefer the repository's existing patterns and packages over creating parallel mechanisms.
- Keep changes focused on the requested task.
- Do not weaken or bypass tests to make them pass.
- When a test exposes a product defect, document the defect rather than encoding an expectation that hides it.
