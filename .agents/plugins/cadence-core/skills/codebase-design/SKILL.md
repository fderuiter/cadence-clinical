---
name: codebase-design
description: "Apply deep module design principles, information hiding, interface leverage, and architectural seams to Cadence .NET 10 services and Vue 3 frontend components. Use when designing new modules, refactoring shallow interfaces, or establishing service boundaries."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Codebase Design Principles

Vocabulary and design rules for creating deep, high-leverage modules across the Cadence architecture using Antigravity inspection and refactoring tools.

## Antigravity Tool Execution Matrix

| Phase                    | Antigravity Tool               | Invocation Pattern                                            | Objective                                             |
| :----------------------- | :----------------------------- | :------------------------------------------------------------ | :---------------------------------------------------- |
| **Seam Discovery**       | `find_by_name` / `grep_search` | Query files across `apps/` and `packages/`                    | Identify shallow modules and leaky abstractions       |
| **Interface Inspection** | `view_file`                    | `AbsolutePath=".../Application/Ports/I*.cs"`, `StartLine=...` | Assess interface surface area vs. hidden complexity   |
| **Refactoring**          | `replace_file_content`         | Target: service/port files, surgical replacement              | Deepen module by collapsing pass-through layers       |
| **Boundary Sentinel**    | `run_command`                  | `CommandLine="dotnet cadence check --gate import-boundaries"` | Enforce no sibling database imports across boundaries |

---

## 1. Deep Modules vs. Shallow Wrappers

A **deep module** provides powerful functionality behind a simple, compact interface. A **shallow module** has an interface that is complex relative to the small amount of logic it implements (e.g. 1-to-1 pass-through repository wrappers or fragmented controller utilities).

- **High Leverage**: Small interface surface area, significant hidden implementation complexity.
- **Information Hiding**: Internal data structures, ORM queries, and protocol graph walks must remain private to the module boundary.
- **REST Seam Boundaries**: Sibling microservices communicate exclusively via typed HTTP endpoints with HMAC gateway tokens—never via sibling database model imports.

---

## 2. C# 14 Ports and Generics

Ports are interfaces in `Application/` (or `Cadence.Kernel.Abstractions` for kernel concepts); adapters live in `Infrastructure/`:

```csharp
public interface IRepository<T>
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task SaveAsync(T entity, CancellationToken cancellationToken);
}
```

---

## 3. Domain Records

- Model values and DTOs as `record` types with primary constructors and `init` accessors; nullable reference types are on and warnings are errors.
- Reference kernel IDs (`StudyId`, `SiteId`, `ParticipantId`), never `string` identifiers or redefined kernel types (spec §2.3).
- Keep validation inside the domain (constructors, FluentValidation validators); serialize with `System.Text.Json` source generation.

---

## 4. Frontend Component Design (`apps/web`)

- **Vanilla CSS Design Tokens**: Use standardized CSS variables (`var(--primary)`, `var(--surface)`, `var(--border)`, `var(--radius-md)`) from `apps/web/src/style.css`. Never use Tailwind utility classes.
- **Full-Width Workspaces**: Design high-density clinical views (SoA Matrix, Gantt visualizer) to occupy the full viewport width. Place secondary inspection panes in collapsible slide-over drawers.
