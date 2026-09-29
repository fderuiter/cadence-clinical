# ==============================================================================
# Cadence Clinical — Developer Convenience Makefile
#
# This Makefile wraps canonical .NET / pnpm CLI commands for developers. All core
# solution convenience targets delegate directly to `dotnet cadence` / Cadence.Cli commands.
# Run `make help` to see all targets.
# ==============================================================================

.DEFAULT_GOAL := help
.PHONY: help setup wizard doctor build dev fix lint-paths typecheck lint format check verify test sync-gxp rtm adr docs ports db-reset db-reset-offline db-seed db-status regenerate-templates

# Colour codes
CYAN  := \033[0;36m
RESET := \033[0m
BOLD  := \033[1m

CADENCE_CLI := dotnet run --project packages/cli/src/Cadence.Cli.csproj --

##@ Setup

setup: ## Install all Node deps and pre-commit hooks
	pnpm install --frozen-lockfile
	@echo "$(CYAN)✔ Environment ready. Run 'make verify' to confirm everything passes.$(RESET)"

wizard: ## Interactive full-stack onboarding wizard (.NET SDK + setup, for new/CI/Codespaces environments)
	bash scripts/setup_wizard.sh

##@ Developer Experience & CLI

build: ## Compile the Cadence solution (.NET 10)
	dotnet build Cadence.sln

doctor: ## Run system diagnostics and auto-heal development environment health (dotnet cadence doctor --auto-fix)
	$(CADENCE_CLI) doctor --auto-fix

dev: ## Orchestrate local microservices with live reloading
	$(CADENCE_CLI) dev

##@ Code Quality

fix: ## Auto-fix all C# code formatting, ADRs, and schemas (safe to run anytime)
	$(CADENCE_CLI) fix

lint-paths: ## Run lightweight path-pattern boundary linter
	dotnet test tests/Cadence.ArchitectureTests.csproj --no-build

typecheck: ## Run static type checking for repository contracts
	dotnet test tests/Cadence.ArchitectureTests.csproj --no-build

lint: lint-paths typecheck ## Check lint (no auto-fix)
	pnpm -r lint

format: ## Check formatting only (no auto-fix)
	dotnet format Cadence.sln --verify-no-changes

check: ## Run all 21 pre-push quality gates concurrently in parallel (dotnet cadence check --parallel)
	$(CADENCE_CLI) check --parallel

verify: ## Full verification — quality gates + all test suites (use before opening a PR)
	$(CADENCE_CLI) check --parallel

test: ## Run backend unit tests with coverage
	$(CADENCE_CLI) test

##@ GxP Compliance

sync-gxp: ## Run tests → regenerate RTM docs → stage docs/SDLC/ (dotnet cadence gxp sync)
	$(CADENCE_CLI) gxp sync

rtm: ## Validate that checked-in RTM docs are up to date (dotnet cadence gxp validate)
	$(CADENCE_CLI) gxp validate

##@ Architecture & Documentation

adr: ## Scaffold a new Architecture Decision Record (prompts for title/domain/req)
	$(CADENCE_CLI) scaffold adr

docs: ## Serve the VitePress documentation portal locally
	pnpm docs:dev

##@ Database

db-reset: ## Drop and re-create the local development database schema
	$(CADENCE_CLI) db reset

db-reset-offline: ## Reset databases offline, generating warnings if remote connections fail
	$(CADENCE_CLI) db reset

db-seed: ## Populate multi-engine databases with realistic end-to-end clinical trial datasets
	$(CADENCE_CLI) db seed

db-status: ## Display multi-engine database sizes and available snapshots
	$(CADENCE_CLI) db status

regenerate-templates: ## Regenerate DOCX protocol templates programmatically
	$(CADENCE_CLI) check --parallel

ports: ## Check that all required service ports are free (dotnet cadence doctor)
	$(CADENCE_CLI) doctor

##@ Help

help: ## Show this help message
	@printf "$(BOLD)Cadence Clinical — Developer Makefile$(RESET)\n\n"
	@printf "Usage: make $(CYAN)<target>$(RESET)\n\n"
	@awk 'BEGIN {FS = ":.*##"; section=""} \
	    /^##@/ { section=substr($$0, 5); printf "\n$(BOLD)%s$(RESET)\n", section } \
	    /^[a-zA-Z_-]+:.*##/ { printf "  $(CYAN)%-15s$(RESET) %s\n", $$1, $$2 }' \
	    $(MAKEFILE_LIST)
	@printf "\n"
