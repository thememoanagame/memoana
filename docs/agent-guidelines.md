# MemoAna Agent Guidelines

This document is the mandatory implementation guide for agents working on MemoAna.

## Source of truth

Before changing code, inspect the existing implementation in the target branch and preserve its established naming, namespaces, project boundaries, feature layout, DI registration and request/response flow. Do not introduce an alternative architecture merely because it is familiar.

The backend is a .NET 10 solution composed of five projects:

- `MemoAna.Backend` — Presentation / Web API + Blazor Interactive Server.
- `MemoAna.Backend.Application` — Application use cases, contracts, requests, responses, handlers and validation.
- `MemoAna.Backend.Composition` — Composition root and dependency injection registration.
- `MemoAna.Backend.Domain` — Domain model and domain abstractions.
- `MemoAna.Backend.Infrastructure` — persistence and external integrations/implementations.

Dependency direction is strictly:

```text
Application -> Domain
Infrastructure -> Application
Composition -> Application
Composition -> Infrastructure
Presentation -> Application
Presentation -> Composition
```

Never reverse these dependencies and never make Domain depend on Infrastructure, Presentation or Composition.

## Feature organization

A feature is treated as an aggregate of patterns across layers. Keep the feature name and responsibility aligned in every applicable layer rather than creating unrelated technical folders.

Typical responsibilities:

- Domain: entities, value objects, domain rules and domain exceptions.
- Application: abstractions/contracts, commands, queries, requests, responses, handlers and validators.
- Infrastructure: concrete services, persistence adapters, external-provider integrations and infrastructure-specific options/models.
- Composition: DI registration, authentication configuration and application-service wiring.
- Presentation: controllers/endpoints and UI integration only; business rules stay out of controllers/components.

Follow existing Identity implementation as the reference for the feature pattern.

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

When changing authentication/authorization, verify both the existing local Identity path and the alternative provider path; adding a provider must not regress password registration/login, refresh, revocation, confirmation or existing authorization policies.
