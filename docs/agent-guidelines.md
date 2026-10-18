# MemoAna Agent Guidelines

This document is the mandatory implementation guide for agents working on MemoAna.

## Source of truth

Before changing code, inspect the existing implementation in the target branch and preserve its established naming, namespaces, project boundaries, feature layout, DI registration and request/response flow. Do not introduce an alternative architecture merely because it is familiar.

## General implementation rules

- Target .NET 10 APIs and current language features.
- Prefer primary constructors where they improve clarity and match surrounding code.
- Use nullable reference types correctly; do not suppress nullability warnings without a justified invariant.
- Prefer exceptions for exceptional/invalid states instead of returning `null` as an error signal.
- Use specific custom exceptions for domain/application/infrastructure failures that must be handled by the global exception pipeline.
- Do not leak provider-specific exceptions or implementation details through Application contracts.
- Propagate `CancellationToken` through every asynchronous application, infrastructure and I/O boundary.
- Do not create `CancellationTokenSource` instances merely to replace a caller's token.
- Never use `.Result`, `.Wait()`, blocking locks around async operations, or fire-and-forget tasks for request work.
- Use `ConfigureAwait` only where there is a concrete reason; do not add it mechanically to ASP.NET Core code.
- Dispose owned `IDisposable`/`IAsyncDisposable` resources deterministically. Prefer `using`/`await using` declarations for local disposable resources.
- Do not dispose services owned by the DI container.
- Avoid unnecessary allocations, duplicate database calls and repeated provider lookups.
- Do not log passwords, refresh tokens, access tokens, provider authorization codes, client secrets or other credentials.
- Do not hard-code secrets, OAuth credentials, signing keys or provider identifiers that are environment-specific.
- Configuration belongs in options objects and is validated at startup where practical.

## Verification

Every implementation must be buildable and testable. Add or update unit tests for new behavior and failure paths. Do not consider a feature complete merely because it compiles.
