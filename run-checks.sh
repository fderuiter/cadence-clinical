#!/bin/bash
set -e

# Run checks inside the execution container where dependencies are installed
# We can use any of the service containers (designer, execution, gateway) as they all share the same image

echo "Running formatting checks (Ruff Format)..."
docker compose -f docker/docker-compose.yml exec execution ruff format --check .

echo "Running linter checks (Ruff)..."
docker compose -f docker/docker-compose.yml exec execution ruff check .

echo "Running tests (Pytest)..."
docker compose -f docker/docker-compose.yml exec execution pytest

echo "Running frontend checks..."
pnpm install
pnpm check

echo "Running markdown link verification..."
node scripts/check-links.js

echo "Running ADR validation..."
python3 scripts/validate_adrs.py

echo "Running Markdown validation..."
python3 scripts/validate_markdown.py
