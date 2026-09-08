# Identity Feature Guidelines

The Identity feature is a cross-layer aggregate of patterns. Preserve its existing structure when extending it.

## Existing contracts

The Application layer exposes `IIdentityService` and Identity request/response abstractions. The Infrastructure layer contains the concrete Identity service, Identity models/options and JWT services. Composition registers ASP.NET Core Identity, EF stores, token services, validators, mediator and JWT authentication. Presentation exposes the HTTP surface.

## Alternative provider rule

Adding Google Play Games authentication must not replace the existing email/password path. Both paths must converge on the local Identity model and application JWT issuance.

Conceptually:

```text
Local credentials --------------------> Local Identity user ---->
                                         Application JWT
Google Play Games credential -> Verify -> Local Identity user ---->
```

The provider is an identity proof mechanism, not the application's session/token authority.

## Account linking/provisioning

A Google identity must have a stable provider-specific subject/player identifier. Do not use display name as the identifier. Email may be used as a lookup/linking attribute only according to a deliberate, verified account-linking policy; never merge accounts silently when ownership is ambiguous.

Define how a first-time Google player is provisioned, how a returning player is resolved, and how an existing password user can be linked. These decisions must be represented in application contracts and tested.

## Provider boundary

Do not reference `Google.Apis.*` from Domain or Application contracts. Google SDK interaction belongs in Infrastructure. The Application layer should depend on a small abstraction expressing the required verification/exchange operation.

Before coding, inspect the installed package versions and their actual APIs. `Google.Apis.Games.v1` and `Google.Apis.Auth` do not by themselves imply a complete Android Google Play Games sign-in client flow. The MAUI client may need a platform-specific Google Play Games sign-in implementation that obtains a server-verifiable credential, while the backend validates that credential. Do not fabricate a client/server protocol or SDK call.

## Token issuance

After successful provider verification and local-user resolution, issue the same application access/refresh token model used by the existing Identity feature. Do not return Google access tokens as the application's bearer token.

## Authorization

Existing authorization policies and JWT validation remain authoritative. Google-authenticated users must receive the same appropriate local claims/permissions as users authenticated through the existing path.

## Failure semantics

Provider credential rejection, invalid audience/client configuration, expired assertions, unknown player identities and ambiguous account-linking cases must have explicit failure semantics. Avoid returning `null` merely to indicate an authentication error when the operation contract can use a typed result or exception handled by the global pipeline.
