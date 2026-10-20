# ==============================================================================
# Cadence Clinical — Developer Convenience Makefile
#
# This Makefile wraps canonical .NET / pnpm CLI commands for developers. All core
# solution convenience targets delegate directly to `dotnet cadence` / Cadence.Cli commands.
# Run `make help` to see all targets.
# ==============================================================================

.DEFAULT_GOAL := help
.PHONY: help setup wizard doctor build dev fix lint-paths typecheck lint format action-pinning check verify ci test sync-gxp gxp-sync gxp-snapshot rtm adr docs ports db-reset db-reset-offline

# Colour codes
CYAN  := \033[0;36m
RESET := \033[0m
BOLD  := \033[1m

CADENCE_CLI := ./bin/cadence

./bin/cadence:
	@mkdir -p ./bin
	dotnet publish packages/cli/src/Cadence.Cli.csproj -c Release --self-contained -o ./bin

##@ Setup

setup: ## Install all Node deps and pre-commit hooks
	pnpm install --frozen-lockfile
	git config core.hooksPath .githooks
	chmod +x .githooks/pre-commit
	@echo "$(CYAN)✔ Environment ready. Run 'make verify' to confirm everything passes.$(RESET)"

wizard: ## Interactive full-stack onboarding wizard (.NET SDK + setup, for new/CI/Codespaces environments)
	bash scripts/setup_wizard.sh

##@ Developer Experience & CLI

build: $(CADENCE_CLI) ## Compile the Cadence solution (.NET 10)
	dotnet build Cadence.sln

doctor: $(CADENCE_CLI) ## Run system diagnostics and auto-heal development environment health (dotnet cadence doctor --auto-fix)
	$(CADENCE_CLI) doctor --auto-fix

dev: $(CADENCE_CLI) ## Start all local services and datastores via the .NET Aspire AppHost
	$(CADENCE_CLI) dev

##@ Code Quality

fix: $(CADENCE_CLI) ## Auto-fix all C# code formatting, ADRs, and schemas (safe to run anytime)
	$(CADENCE_CLI) fix

lint-paths: ## Run lightweight path-pattern boundary linter
	dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj --no-build

typecheck: ## Run static type checking for repository contracts
	dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj --no-build

lint: lint-paths typecheck ## Check lint (no auto-fix)
	pnpm -r lint

format: ## Check formatting only (no auto-fix)
	dotnet format Cadence.sln --verify-no-changes

action-pinning: ## Validate GitHub Actions commit SHA pinning
	python3 scripts/check-action-pinning.py

check: $(CADENCE_CLI) ## Run every quality gate (cadence check)
	$(CADENCE_CLI) check

verify: $(CADENCE_CLI) ## Full verification — quality gates + all test suites (use before opening a PR)
	$(CADENCE_CLI) check
	$(CADENCE_CLI) test

ci: $(CADENCE_CLI) ## Run the CI workflow's jobs locally (GitHub Actions is off until 2027; needs PostgreSQL)
	$(CADENCE_CLI) ci

test: $(CADENCE_CLI) ## Run backend unit tests with coverage
	$(CADENCE_CLI) test

##@ GxP Compliance

sync-gxp: $(CADENCE_CLI) ## Run tests and validate requirement tags on a branch; writes a preview, never docs/SDLC/ (cadence gxp sync)
	$(CADENCE_CLI) gxp sync

gxp-snapshot: $(CADENCE_CLI) ## Integration owner only: run tests, write and stage the docs/SDLC/ snapshot (ADR-2532)
	CADENCE_GXP_SNAPSHOT=1 $(CADENCE_CLI) gxp snapshot

gxp-sync: sync-gxp ## Alias for sync-gxp

rtm: $(CADENCE_CLI) ## Validate requirement tags and the RTM integrity stamps (cadence gxp validate)
	$(CADENCE_CLI) gxp validate

##@ Architecture & Documentation

adr: $(CADENCE_CLI) ## Scaffold a new Architecture Decision Record (make adr TITLE="Use X for Y")
	@test -n "$(TITLE)" || { echo "Usage: make adr TITLE=\"Use X for Y\""; exit 1; }
	$(CADENCE_CLI) scaffold adr "$(TITLE)"

docs: ## Serve the VitePress documentation portal locally
	pnpm docs:dev

##@ Database

db-reset: $(CADENCE_CLI) ## Drop and re-create the local development database schema
	$(CADENCE_CLI) db reset

db-reset-offline: $(CADENCE_CLI) ## Reset databases offline, generating warnings if remote connections fail
	$(CADENCE_CLI) db reset

ports: $(CADENCE_CLI) ## Check that all required service ports are free (cadence check-ports)
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
