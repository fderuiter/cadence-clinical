# ==============================================================================
# Cadence Clinical — Developer Convenience Makefile
#
# This Makefile provides native .NET 10 CLI automation and wraps canonical
# pnpm/uv scripts for developers. All core solution targets delegate directly
# to dotnet CLI or Cadence.Cli commands. Run `make help` to see all targets.
# ==============================================================================

.DEFAULT_GOAL := help
.PHONY: help setup wizard doctor build dev fix lint-paths typecheck lint format check verify test sync-gxp rtm adr docs ports db-reset db-reset-offline db-seed db-status regenerate-templates

# Colour codes
CYAN  := \033[0;36m
RESET := \033[0m
BOLD  := \033[1m

##@ Setup

setup: ## Install all Python + Node deps, Playwright browsers, and pre-commit hooks
	uv sync --python 3.14 --all-extras
	uv run --python 3.14 playwright install chromium
	uv run --python 3.14 pre-commit install --install-hooks
	pnpm install --frozen-lockfile
	@echo "$(CYAN)✔ Environment ready. Run 'make verify' to confirm everything passes.$(RESET)"

wizard: ## Interactive full-stack onboarding wizard (.NET SDK + setup, for new/CI/Codespaces environments)
	bash scripts/setup_wizard.sh

##@ Developer Experience & CLI

build: ## Compile the Cadence solution (.NET 10)
	dotnet build Cadence.sln

doctor: ## Run system diagnostics and verify development environment health
	dotnet run --project packages/cli/src/Cadence.Cli.csproj -- doctor

dev: ## Orchestrate local microservices with live reloading
	uv run --python 3.14 cadence dev

##@ Code Quality

fix: ## Auto-fix all lint violations, format code, and sync schemas (safe to run anytime)
	pnpm -r format
	uv run --python 3.14 ruff check --fix . --exclude apps/execution/database/models.py
	uv run --python 3.14 ruff format --target-version py313 . --exclude apps/execution/database/models.py
	dotnet format Cadence.sln

lint-paths: ## Run lightweight path-pattern boundary linter
	uv run --python 3.14 python scripts/validate_path_patterns.py --all

typecheck: ## Run static type checking for repository contracts
	uv run --python 3.14 python scripts/verify_contracts.py

lint: lint-paths typecheck ## Check lint (no auto-fix)
	pnpm -r lint
	uv run --python 3.14 ruff check . --exclude apps/execution/database/models.py

format: ## Format C# code, frontend code, and Python scripts
	pnpm -r format
	uv run --python 3.14 ruff format --target-version py313 . --exclude apps/execution/database/models.py
	dotnet format Cadence.sln

check: ## Run all pre-push quality gates: format, ADRs, architecture sentinels
	dotnet run --project packages/cli/src/Cadence.Cli.csproj -- check

verify: ## Full verification — quality gates + all test suites (use before opening a PR)
	pnpm verify

test: ## Run backend and solution unit test suites
	dotnet test Cadence.sln

##@ GxP Compliance

sync-gxp: ## Run tests → regenerate RTM docs → stage docs/SDLC/ (fixes the CI compliance gate)
	dotnet run --project packages/cli/src/Cadence.Cli.csproj -- gxp sync

rtm: ## Validate that checked-in RTM docs are up to date (read-only, no test run)
	dotnet run --project packages/cli/src/Cadence.Cli.csproj -- gxp validate

##@ Architecture & Documentation

adr: ## Scaffold a new Architecture Decision Record (prompts for title/domain/req)
	uv run --python 3.14 python scripts/create_adr.py

docs: ## Serve the VitePress documentation portal locally
	pnpm docs:dev

##@ Database

db-reset: ## Drop and re-create the local development database schema
	uv run --python 3.14 cadence db reset

db-reset-offline: ## Reset databases offline, generating warnings if remote connections fail
	uv run --python 3.14 cadence db reset --allow-offline

db-seed: ## Populate multi-engine databases with realistic end-to-end clinical trial datasets
	uv run --python 3.14 cadence db seed

db-status: ## Display multi-engine database sizes and available snapshots
	uv run --python 3.14 cadence db status

regenerate-templates: ## Regenerate DOCX protocol templates programmatically
	uv run --python 3.14 python scripts/regenerate_templates.py

ports: ## Check that all required service ports are free
	dotnet run --project packages/cli/src/Cadence.Cli.csproj -- doctor

##@ Help

help: ## Show this help message
	@printf "$(BOLD)Cadence Clinical — Developer Makefile$(RESET)\n\n"
	@printf "Usage: make $(CYAN)<target>$(RESET)\n\n"
	@awk 'BEGIN {FS = ":.*##"; section=""} \
	    /^##@/ { section=substr($$0, 5); printf "\n$(BOLD)%s$(RESET)\n", section } \
	    /^[a-zA-Z_-]+:.*##/ { printf "  $(CYAN)%-15s$(RESET) %s\n", $$1, $$2 }' \
	    $(MAKEFILE_LIST)
	@printf "\n"

