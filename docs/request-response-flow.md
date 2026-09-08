# MemoAna Feature Request/Response Flow

This document defines the canonical request/response flow for backend features. It complements `agent-guidelines.md` and the existing Identity implementation.

## Canonical flow

```text
HTTP Request Model
      |
      v
Presentation / Controller
      |  maps request -> Command/Query
      v
Application / Mediator
      |  dispatches
      v
Command/Query Handler
      |  calls service abstraction
      v
Application Service Abstraction
      |
      v
Infrastructure Service Implementation
      |  performs operation
      |  repository access when persistence is required
      v
DTO result
      |
      v
Handler
      |  encapsulates DTO -> Response for Command/Query
      v
Controller
      |
      v
HTTP Response
```

## Presentation

Controllers are transport adapters. They receive request models and translate them into the corresponding Application command/query before dispatching through Mediator. Controllers must not contain business rules, persistence logic, provider SDK calls or direct service implementation calls.

The controller returns the response produced by the Application flow and is responsible only for HTTP concerns such as route binding, status-code semantics and request-model mapping.

## Application

Each use case is represented by a Command or Query and its corresponding Handler. The Handler owns orchestration of the use case and depends on an Application-layer service abstraction, never on an Infrastructure implementation.

The Handler dispatches the operation through that abstraction and receives a DTO representing the operation result. The DTO is then encapsulated in the Response type associated with the Command/Query and returned to the controller.

Provider-specific SDK types, database entities and infrastructure implementation details must not cross this boundary.

## Infrastructure

Infrastructure implements the Application service abstractions. It performs the actual external integration, authentication operation or persistence operation.

When SQL or NoSQL data is required, Infrastructure must use the project's generic repository abstraction. Repository access is constrained to entity types registered in the corresponding SQL/NoSQL DbContext. Do not introduce ad-hoc persistence access that bypasses the established repository/unit-of-work pattern unless the existing architecture explicitly requires it.

Infrastructure returns an Application-facing DTO rather than exposing persistence entities or provider SDK models.

## DTO and Response separation

A service result DTO represents the data returned by the service operation. A Command/Query Response represents the Application contract exposed by that use case. Keep these concepts separate: the Handler is the boundary that encapsulates the service DTO in the use-case response.

## Error handling

Do not use `null` as a generic failure signal. Invalid or exceptional states should use the project's exception strategy and global exception pipeline. Provider-specific exceptions must be translated at the Infrastructure boundary into stable application/infrastructure exceptions as appropriate.

## Cancellation and async

The request cancellation token must flow from the controller through Mediator, Handler, service abstraction, Infrastructure implementation, repository and external I/O. All I/O operations are asynchronous. Never block with `.Result` or `.Wait()` and never create fire-and-forget request work.

## Authentication provider features

Alternative authentication providers must follow exactly the same flow. For example, Google Play Games authentication should be exposed as a Presentation request, mapped to an Application command, handled through an Application service abstraction, implemented in Infrastructure, resolved to the local Identity user, and finally converted into the application's own JWT/session response. Google SDK types must remain in Infrastructure/client-platform boundaries.

## Implementation rule

When implementing a new feature, first inspect the complete existing Identity feature and mirror its established command/query, handler, service abstraction, DTO, response, controller, DI and test organization. Do not create a parallel architectural pattern.
