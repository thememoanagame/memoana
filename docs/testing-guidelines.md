# Testing Guidelines

Tests live under `tests/web/MemoAna.Backend.UnitTests` and already include Identity fixtures. Extend those patterns instead of creating a separate test architecture.

## Required coverage for feature work

- Application handlers: successful execution, validation failures and relevant exception paths.
- Infrastructure provider adapter: valid credential, invalid/expired credential, provider/API failure and cancellation.
- Identity integration: first-time provider user, returning provider user, existing local user/linking behavior and duplicate/ambiguous identity cases.
- Token behavior: successful issuance and regression coverage for existing JWT validation/revocation.
- Authorization: provider-authenticated users receive the same local authorization semantics as password-authenticated users.

Use isolated test data and avoid external Google services in unit tests. Provider SDK calls should be behind an abstraction so tests can use deterministic fakes/mocks.

## Regression rule

Do not weaken existing Identity tests to make provider authentication pass. A new authentication path is additive and must preserve the behavior of local registration/login, refresh, revoke, email confirmation, password recovery/reset and two-factor features.

## Cancellation

Include cancellation tests where cancellation is part of the provider/application contract. A cancellation request should propagate rather than being converted into a generic authentication failure.
