---
name: codebase-design
description: "Apply deep module design principles, information hiding, interface leverage, and architectural seams to Cadence Python backends and Vue 3 frontend components. Use when designing new modules, refactoring shallow interfaces, or establishing service boundaries."
license: MIT
metadata:
  author: Cadence Team
  version: "2.0"
---

# Cadence Codebase Design Principles

Vocabulary and design rules for creating deep, high-leverage modules across the Cadence architecture using Antigravity inspection and refactoring tools.

## Antigravity Tool Execution Matrix

| Phase | Antigravity Tool | Invocation Pattern | Objective |
| :--- | :--- | :--- | :--- |
| **Seam Discovery** | `find_by_name` / `grep_search` | Query files across `apps/` and `packages/` | Identify shallow modules and leaky abstractions |
| **Interface Inspection**| `view_file` | `AbsolutePath=".../ports.py"`, `StartLine=...` | Assess interface surface area vs. hidden complexity |
| **Refactoring** | `replace_file_content` | Target: service/port files, surgical replacement | Deepen module by collapsing pass-through layers |
| **Boundary Sentinel** | `run_command` | `CommandLine="uv run cadence check --gate import-boundaries"` | Enforce no sibling database imports across boundaries |

---

## 1. Deep Modules vs. Shallow Wrappers

A **deep module** provides powerful functionality behind a simple, compact interface. A **shallow module** has an interface that is complex relative to the small amount of logic it implements (e.g. 1-to-1 pass-through repository wrappers or fragmented controller utilities).

- **High Leverage**: Small interface surface area, significant hidden implementation complexity.
- **Information Hiding**: Internal data structures, ORM queries, and protocol graph walks must remain private to the module boundary.
- **REST Seam Boundaries**: Sibling microservices communicate exclusively via typed HTTP endpoints with HMAC gateway tokens—never via sibling database model imports.

---

## 2. Python 3.14+ Typing & PEP 695 Generics

Use native type parameter syntax rather than inheriting from `typing.Generic`:

```python
# ✔ CORRECT — PEP 695 native type parameter syntax
from abc import ABC, abstractmethod


class RepositoryPort[T](ABC):

    @abstractmethod
    async def get_by_id(self, entity_id: str) -> T | None: ...

    @abstractmethod
    async def save(self, entity: T) -> T: ...
```

---

## 3. Pydantic v2 Domain Models

- Models must use strict typing (avoid `Any`).
- Keep validation and normalization rules encapsulated within domain schemas using `@model_validator` and `@field_validator`.
- Expose clear serialization interfaces (`model_dump()`, `model_dump_json()`).

---

## 4. Frontend Component Design (`apps/web`)

- **Vanilla CSS Design Tokens**: Use standardized CSS variables (`var(--primary)`, `var(--surface)`, `var(--border)`, `var(--radius-md)`) from `apps/web/src/style.css`. Never use Tailwind utility classes.
- **Full-Width Workspaces**: Design high-density clinical views (SoA Matrix, Gantt visualizer) to occupy the full viewport width. Place secondary inspection panes in collapsible slide-over drawers.

