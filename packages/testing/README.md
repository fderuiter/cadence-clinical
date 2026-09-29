# Testing Package

## Overview

Cadence Clinical Centralized Test Infrastructure & Domain Factories

---

## Architectural Boundaries

The `testing` package operates as a modular shared library within the Cadence Clinical ecosystem:
- **Code-Adjacent Domain:** Located strictly within `packages/testing/`.
- **Decoupled Architecture:** Provides explicit interface contracts and reusable domain models without imposing direct database or cross-service runtime bindings.
- **GxP Audit Compliance:** Maintained under strict versioning and change control standards.

---

## Public APIs / Exports

Key exported C# interfaces and components:
- `SubjectFactory`
- `ProtocolDefinitionFactory`
- `ClinicalObservationFactory`
- `QueryDiscrepancyFactory`
- `ConsentRecordFactory`
- `DocumentMetadataFactory`
- `AuditLogFactory`
- `InMemoryStoragePort`
- `FakeAccessibilityValidator`
- `InMemoryNotificationDispatcher`
- `InMemoryRepository<TEntity, TId>`
- `TestSecurityContextFactory`
- `TestDependencyGraph`

---

## Dependencies

This package relies on the following standard .NET 10 projects and libraries:
- `Cadence.Core.Domain`
- `Cadence.Security`
- `Cadence.Storage`
- `Cadence.Ddf`
- `Bogus`
- `Microsoft.CodeAnalysis.CSharp`

---

## Local Development & Testing

Run unit tests for this package and the entire solution:

```bash
dotnet test Cadence.sln
```
