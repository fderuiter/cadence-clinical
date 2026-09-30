# Cadence.Hexagonal

Generic building blocks for ports-and-adapters code: entity and value-object base types, domain events, port interfaces, domain exceptions, and an exception-to-ProblemDetails mapper.

## Project

- Project: `packages/hexagonal/src/Cadence.Hexagonal.csproj` (assembly `Cadence.Hexagonal`, namespace `Cadence.Hexagonal`).
- `DomainPrimitives.cs`: `BaseEntity`, `AggregateRoot`, `ValueObject`, `DomainEvent`.
- `Ports.cs`: `IRepositoryPort`, `IUseCasePort`, `IEventDispatcherPort`, `IAuditLoggerPort`, `IExternalServiceClientPort`.
- `DomainExceptions.cs`: `DomainException` and subclasses (`EntityNotFoundException`, `EntityAlreadyExistsException`, `ValidationException`, `ConflictException`, `PreconditionFailedException`, `UnauthorizedActionException`, `DatabaseException`).
- `ProblemDetails.cs`: `ProblemDetails` record and `DomainErrorMapper`.

## Key dependencies

None beyond the .NET base library.

## Referenced by

The three test projects only. No application or package uses it; the services use `Cadence.Core.Domain` and ASP.NET Core `ProblemDetails` instead.

## Tests

Covered by `tests/Packages/PortedPackagesUnitTests.cs`:

```bash
dotnet test tests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Packages.PortedPackagesUnitTests"
```
