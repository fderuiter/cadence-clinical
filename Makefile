# ==============================================================================
# Cadence Clinical — Developer Convenience Makefile
#
# This Makefile wraps canonical .NET / pnpm CLI commands for developers. All core
# CLI targets use the same entry point as `pnpm cadence`.
# Run `make help` to see all targets.
# ==============================================================================

.DEFAULT_GOAL := help
.PHONY: help setup wizard doctor build dev fix lint-paths typecheck lint format action-pinning check verify ci test sync-gxp gxp-sync gxp-snapshot rtm adr docs ports db-reset db-reset-offline

# Colour codes
CYAN  := \033[0;36m
RESET := \033[0m
BOLD  := \033[1m

CADENCE_CLI := pnpm cadence

##@ Setup

setup: ## Install all Node deps and pre-commit hooks
	pnpm install --frozen-lockfile
	git config core.hooksPath .githooks
	chmod +x .githooks/pre-commit
	@echo "$(CYAN)✔ Environment ready. Run 'make verify' to confirm everything passes.$(RESET)"

wizard: ## Interactive full-stack onboarding wizard (.NET SDK + setup, for new/CI/Codespaces environments)
	bash scripts/setup_wizard.sh

##@ Developer Experience & CLI

build: ## Prepare the Cadence CLI and compile the .NET solution
	$(CADENCE_CLI) --version
	dotnet build Cadence.sln

doctor: ## Run system diagnostics and auto-heal development environment health
	$(CADENCE_CLI) doctor --auto-fix

dev: ## Start all local services and datastores via the .NET Aspire AppHost
	$(CADENCE_CLI) dev

##@ Code Quality

fix: ## Auto-fix all C# code formatting, ADRs, and schemas
	$(CADENCE_CLI) fix

lint-paths: ## Run the path-pattern boundary linter
	$(CADENCE_CLI) validate-path-patterns

typecheck: ## Type-check the web workspace
	pnpm --filter web typecheck

lint: lint-paths typecheck ## Check lint (no auto-fix)
	pnpm -r lint

format: ## Check formatting only (no auto-fix)
	dotnet format Cadence.sln --verify-no-changes

action-pinning: ## Validate GitHub Actions workflow security and SHA pinning
	$(CADENCE_CLI) check workflow-security

check: ## Run every quality gate (cadence check)
	$(CADENCE_CLI) check

verify: ## Run the quality gates and .NET test suites
	$(CADENCE_CLI) check
	$(CADENCE_CLI) test

ci: ## Run the CI workflow's jobs locally (GitHub Actions is off until 2027; needs PostgreSQL)
	$(CADENCE_CLI) ci

test: ## Run the .NET test suites
	$(CADENCE_CLI) test

##@ GxP Compliance

sync-gxp: ## Run tests and validate requirement tags on a branch; writes a preview, never docs/SDLC/ (cadence gxp sync)
	$(CADENCE_CLI) gxp sync

gxp-snapshot: ## Integration owner only: run tests, write and stage the docs/SDLC/ snapshot (ADR-2532)
	CADENCE_GXP_SNAPSHOT=1 $(CADENCE_CLI) gxp snapshot

gxp-sync: sync-gxp ## Alias for sync-gxp

rtm: ## Validate requirement tags and the RTM integrity stamps (cadence gxp validate)
	$(CADENCE_CLI) gxp validate

##@ Architecture & Documentation

adr: ## Scaffold a new Architecture Decision Record (make adr TITLE="Use X for Y")
	@test -n "$(TITLE)" || { echo "Usage: make adr TITLE=\"Use X for Y\""; exit 1; }
	$(CADENCE_CLI) scaffold adr "$(TITLE)"

docs: ## Serve the VitePress documentation portal locally
	pnpm docs:dev

##@ Database

db-reset: ## Drop and re-create the local development database schema
	$(CADENCE_CLI) db reset

db-reset-offline: db-reset ## Alias for db-reset

ports: ## Check that all required service ports are free (cadence check-ports)
	$(CADENCE_CLI) check-ports

##@ Help

help: ## Show this help message
	@printf "$(BOLD)Cadence Clinical — Developer Makefile$(RESET)\n\n"
	@printf "Usage: make $(CYAN)<target>$(RESET)\n\n"
	@awk 'BEGIN {FS = ":.*##"; section=""} \
	    /^##@/ { section=substr($$0, 5); printf "\n$(BOLD)%s$(RESET)\n", section } \
	    /^[a-zA-Z_-]+:.*##/ { printf "  $(CYAN)%-15s$(RESET) %s\n", $$1, $$2 }' \
	    $(MAKEFILE_LIST)
	@printf "\n"
