# Original User Request

## 2026-08-07T18:31:53Z

<USER_REQUEST>
# Teamwork Project Prompt — Launched

> Status: Launched
> Goal: Craft prompt → get user approval → delegate to teamwork_preview

Refactor the architecture to eliminate `packages/core-models`, moving domain models to their respective owning services and implementing Anti-Corruption Layers (ACLs) for cross-service communication.

Working directory: /Users/fred/Code/cadence-clinical
Integrity mode: demo

## Requirements

### R1. Eradicate `packages/core-models`
Move all domain models currently in `packages/core-models` to the `src/domain/` folder of the service that rightfully owns them (e.g., `execution`, `designer`). Infer ownership based on context and usage.

### R2. Implement Anti-Corruption Layers (ACLs)
If Service A (e.g., execution) needs to process data from Service B (e.g., ctms), Service A must define its own Anti-Corruption Layer (ACL) via local Pydantic DTOs, rather than importing Service B's database models.

## Acceptance Criteria

### Structural Integrity
- [ ] The directory `packages/core-models` no longer exists.
- [ ] No service imports database models from another service (e.g. `execution` does not import from `designer`).
- [ ] Cross-service data processing is handled exclusively through local Pydantic DTOs.

### Verification
- [ ] The entire test suite must pass cleanly (`uv run pytest -n auto`).
- [ ] Code formatting and linting must pass cleanly (`uv run ruff check .` and `uv run ruff format .`).
</USER_REQUEST>

## 2026-08-07T19:23:35Z

<USER_REQUEST>
# Teamwork Project Prompt — Launched

> Status: Launched
> Goal: Craft prompt → get user approval → delegate to teamwork_preview

Refactor the architecture to eliminate `packages/core-models`, moving domain models to their respective owning services and implementing Anti-Corruption Layers (ACLs) for cross-service communication.

Working directory: /Users/fred/Code/cadence-clinical
Integrity mode: demo

## Requirements

### R1. Eradicate `packages/core-models`
Move all domain models currently in `packages/core-models` to the `src/domain/` folder of the service that rightfully owns them (e.g., `execution`, `designer`). Infer ownership based on context and usage.

### R2. Implement Anti-Corruption Layers (ACLs)
If Service A (e.g., execution) needs to process data from Service B (e.g., ctms), Service A must define its own Anti-Corruption Layer (ACL) via local Pydantic DTOs, rather than importing Service B's database models.

### R3. Resource Constraints
You must not spawn more than 5 subagents concurrently at any given point to prevent memory exhaustion on the host machine.

## Acceptance Criteria

### Structural Integrity
- [ ] The directory `packages/core-models` no longer exists.
- [ ] No service imports database models from another service (e.g. `execution` does not import from `designer`).
- [ ] Cross-service data processing is handled exclusively through local Pydantic DTOs.

### Verification
- [ ] The entire test suite must pass cleanly (`uv run pytest -n auto`).
- [ ] Code formatting and linting must pass cleanly (`uv run ruff check .` and `uv run ruff format .`).
- [ ] Maximum concurrency of 5 subagents was respected throughout the run.
</USER_REQUEST>

## 2026-08-07T20:05:43Z

[CRITICAL] Please PAUSE all operations immediately. Instruct the Orchestrator to halt all subagents and stop making API calls for the next 6 minutes. Do not resume work until you receive an explicit RESUME signal from me.

## 2026-08-07T20:12:38Z

<USER_REQUEST>
# Teamwork Project Prompt — Launched

> Status: Launched
> Goal: Craft prompt → get user approval → delegate to teamwork_preview

Refactor the architecture to eliminate `packages/core-models`, moving domain models to their respective owning services and implementing Anti-Corruption Layers (ACLs) for cross-service communication.

Working directory: /Users/fred/Code/cadence-clinical
Integrity mode: demo

## Requirements

### R1. Eradicate `packages/core-models`
Move all domain models currently in `packages/core-models` to the `src/domain/` folder of the service that rightfully owns them (e.g., `execution`, `designer`). Infer ownership based on context and usage.

### R2. Implement Anti-Corruption Layers (ACLs)
If Service A (e.g., execution) needs to process data from Service B (e.g., ctms), Service A must define its own Anti-Corruption Layer (ACL) via local Pydantic DTOs, rather than importing Service B's database models.

### R3. Resource Constraints
You must not spawn more than 5 subagents concurrently at any given point to prevent memory exhaustion on the host machine.

## Acceptance Criteria

### Structural Integrity
- [ ] The directory `packages/core-models` no longer exists.
- [ ] No service imports database models from another service (e.g. `execution` does not import from `designer`).
- [ ] Cross-service data processing is handled exclusively through local Pydantic DTOs.

### Verification
- [ ] The entire test suite must pass cleanly (`uv run pytest -n auto`).
- [ ] Code formatting and linting must pass cleanly (`uv run ruff check .` and `uv run ruff format .`).
- [ ] Maximum concurrency of 5 subagents was respected throughout the run.
</USER_REQUEST>
