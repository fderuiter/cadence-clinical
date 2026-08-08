# BRIEFING — 2026-08-08T01:49:13Z

## Mission
Hexagonal Architecture migration and high-complexity refactoring for `designer` and `tickets` microservices (R4).

## 🔒 My Identity
- Archetype: implementer, qa, specialist
- Roles: implementer, qa, specialist
- Working directory: /Users/fred/Code/cadence-clinical/.agents/worker_4
- Original parent: 1061d95b-859d-448c-a5aa-d1ebf08227f3
- Milestone: R4 Hexagonal Architecture Refactoring (apps/designer and apps/tickets)

## 🔒 Key Constraints
- 4 flat layers: `domain/`, `application/`, `infrastructure/`, `presentation/`
- All repository ports in `domain/` MUST inherit from `packages.hexagonal.RepositoryPort`
- Extract massive 5,788-line `apps/designer/main.py` into routers, domain, application services, and infrastructure repositories. `main.py` must contain ONLY FastAPI setup, DB driver lifecycle, middleware, and `app.include_router(...)`.
- Delete any legacy `src/` directory in designer/tickets.
- Compliance with AGENTS.md (ruff check, ruff format, I001 import sorting, E712 boolean filter pattern, zero sibling db imports, `sync_gxp.py` if tests changed).

## Current Parent
- Conversation ID: 1061d95b-859d-448c-a5aa-d1ebf08227f3
- Updated: 2026-08-08T01:49:13Z

## Task Summary
- **What to build**: Refactor `apps/designer` and `apps/tickets` to flat 4-layer Hexagonal Architecture (`domain/`, `application/`, `infrastructure/`, `presentation/`).
- **Success criteria**: All tests in `apps/designer` and `apps/tickets` pass; archon tests pass; ruff check & format pass; `validate_imports.py` passes; `main.py` pruned.
- **Interface contracts**: `docs/adr/2026-08-08-hexagonal-architecture-standard.md`
- **Code layout**: 4 flat layers (`domain/`, `application/`, `infrastructure/`, `presentation/`)

## Change Tracker
- **Files modified**: [TBD]
- **Build status**: [TBD]
- **Pending issues**: None

## Quality Status
- **Build/test result**: [TBD]
- **Lint status**: [TBD]
- **Tests added/modified**: [TBD]

## Loaded Skills
- None

## Key Decisions Made
- Starting investigation of existing `apps/designer/` and `apps/tickets/` structure.

## Artifact Index
- `/Users/fred/Code/cadence-clinical/.agents/worker_4/BRIEFING.md`
- `/Users/fred/Code/cadence-clinical/.agents/worker_4/DISPATCH.md`
