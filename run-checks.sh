#!/bin/bash
set -e

# Run checks inside the execution container where dependencies are installed
# We can use any of the service containers (designer, execution, gateway) as they all share the same image

echo "Running formatting checks (Black)..."
docker compose -f docker/docker-compose.yml exec execution black --check .

echo "Running linter checks (Ruff)..."
docker compose -f docker/docker-compose.yml exec execution ruff check .

echo "Running tests (Pytest)..."
docker compose -f docker/docker-compose.yml exec execution pytest

echo "Running frontend checks..."
pnpm install
pnpm check
