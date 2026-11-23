# Cadence.Hexagonal

Generic building blocks for ports-and-adapters code: entity and value-object
base types, domain events, port interfaces, domain exceptions, and an
exception-to-ProblemDetails mapper.

## Project

- Project: `packages/hexagonal/src/Cadence.Hexagonal.csproj` (assembly
  `Cadence.Hexagonal`, namespace `Cadence.Hexagonal`).
- `DomainPrimitives.cs`: `BaseEntity`, `AggregateRoot`, `ValueObject`,
  `DomainEvent`.
- `Ports.cs`: `IRepositoryPort`, `IUseCasePort`, `IEventDispatcherPort`,
  `IAuditLoggerPort`, `IExternalServiceClientPort`.
- `DomainExceptions.cs`: `DomainException` and subclasses
  (`EntityNotFoundException`, `EntityAlreadyExistsException`,
  `ValidationException`, `ConflictException`, `PreconditionFailedException`,
  `UnauthorizedActionException`, `DatabaseException`).
- `ProblemDetails.cs`: `ProblemDetails` record and `DomainErrorMapper`.
- Time (K-TIME-3): the project bans `DateTime.Now/UtcNow`.
  `DomainEvent.OccurredAt` is `required`, and `BaseEntity`/`AggregateRoot` take
  `createdAt` from the caller.

## Key dependencies

None beyond the .NET base library.

## Referenced by

The three test projects only. No application or package uses it; the services
use `Cadence.Core.Domain` and ASP.NET Core `ProblemDetails` instead.

## Tests

Covered by `tests/Cadence.UnitTests/Packages/PortedPackagesUnitTests.cs`:

```bash
dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Packages.PortedPackagesUnitTests"
```
