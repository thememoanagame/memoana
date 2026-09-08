# .NET 10 Implementation Guidelines

## Language and APIs

- Target `net10.0`.
- Prefer modern C# constructs already compatible with the repository, including primary constructors where appropriate.
- Keep APIs explicit and strongly typed.
- Enable and respect nullable reference types.
- Prefer `ArgumentNullException.ThrowIfNull`, `ThrowIfNullOrEmpty` and `ThrowIfNullOrWhiteSpace` where appropriate to establish invariants early.

## Async

- Any operation involving network, database, filesystem or other asynchronous I/O must be asynchronous end-to-end.
- Accept and propagate `CancellationToken`.
- Pass the token to EF Core and provider APIs whenever supported.
- Do not block asynchronous work with `.Result`, `.Wait()` or equivalent.
- Do not silently swallow `OperationCanceledException`.
- Do not use `Task.Run` to make naturally asynchronous server I/O appear asynchronous.
- Avoid fire-and-forget operations in HTTP request processing.

## Resource lifetime

- Use `using` for synchronously disposable local resources.
- Use `await using` for asynchronously disposable local resources.
- Prefer using declarations when they improve readability.
- Do not dispose instances created and owned by the dependency-injection container.
- If a type owns an unmanaged or disposable resource, implement the appropriate dispose pattern and test its lifetime.

## Exceptions and nullability

- `null` means absence only when absence is a legitimate part of the contract.
- Do not return nullable values as a substitute for an error condition.
- When an operation cannot fulfill its invariant, throw a meaningful exception that the global handler can map to an appropriate HTTP response.
- Create feature-specific exceptions when existing exceptions do not express the failure accurately.
- Preserve the original exception as an inner exception when wrapping infrastructure failures.
- Never expose internal exception details or sensitive provider information in API responses.

## Security

- Treat credentials and external-provider assertions as untrusted input until verified server-side.
- Never log passwords, access tokens, refresh tokens, authorization codes, client secrets or signing keys.
- Store secrets in the repository's configured secret mechanism/environment, not source code.
- Validate external identities before creating or linking local users.
- Keep authorization decisions in server-side code; never rely on client claims without validating the application's JWT.

## Testing

Tests should cover successful behavior, invalid input, authorization, duplicate identities, provider failures, cancellation and relevant resource/error paths. Authentication changes must include regression coverage for the existing local Identity flow.
