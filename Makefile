# ==============================================================================
# Cadence Clinical — Developer Convenience Makefile
#
# This Makefile wraps the canonical pnpm/uv scripts for developers who prefer
# make. All targets delegate to those scripts so there is a single source of
# truth. Run `make help` to see all available targets.
# ==============================================================================

.DEFAULT_GOAL := help
.PHONY: help setup fix lint format check verify test sync-gxp rtm adr docs ports regenerate-templates

# Colour codes
CYAN  := \033[0;36m
RESET := \033[0m
BOLD  := \033[1m

##@ Setup

setup: ## Install all Python + Node deps, Playwright browsers, and pre-commit hooks
	uv sync --python 3.11 --all-extras
	uv run playwright install chromium
	uv run pre-commit install --install-hooks
	pnpm install --frozen-lockfile
	@echo "$(CYAN)✔ Environment ready. Run 'make verify' to confirm everything passes.$(RESET)"

##@ Code Quality

fix: ## Auto-fix all ruff lint violations and reformat code (safe to run anytime)
	uv run ruff check . --fix
	uv run ruff format .
	@echo "$(CYAN)✔ Lint + format complete.$(RESET)"

lint: ## Check lint (no auto-fix)
	pnpm -r lint
	uv run ruff check . --exclude apps/execution/database/models.py

format: ## Check formatting only (no auto-fix)
	pnpm -r format
	uv run ruff format --check . --exclude apps/execution/database/models.py

check: ## Run all pre-push quality gates: format, lint, secrets, ADRs, markdown, security
	pnpm check

verify: ## Full verification — quality gates + all test suites (use before opening a PR)
	pnpm verify

test: ## Run backend unit tests with coverage
	uv run pytest -n auto

##@ GxP Compliance

sync-gxp: ## Run tests → regenerate RTM docs → stage docs/SDLC/ (fixes the CI compliance gate)
	uv run python scripts/sync_gxp.py

rtm: ## Validate that checked-in RTM docs are up to date (read-only, no test run)
	uv run python scripts/generate_rtm.py --validate

##@ Architecture & Documentation

adr: ## Scaffold a new Architecture Decision Record  (prompts for title/domain/req)
	python3 scripts/create_adr.py

docs: ## Serve the VitePress documentation portal locally
	pnpm docs:dev

##@ Database

db-reset: ## Drop and re-create the local development database schema
	uv run python scripts/reset_db.py

regenerate-templates: ## Regenerate DOCX protocol templates programmatically
	uv run python scripts/regenerate_templates.py

ports: ## Check that all required service ports are free
	python3 scripts/check_ports.py

##@ Help

help: ## Show this help message
	@printf "$(BOLD)Cadence Clinical — Developer Makefile$(RESET)\n\n"
	@printf "Usage: make $(CYAN)<target>$(RESET)\n\n"
	@awk 'BEGIN {FS = ":.*##"; section=""} \
	    /^##@/ { section=substr($$0, 5); printf "\n$(BOLD)%s$(RESET)\n", section } \
	    /^[a-zA-Z_-]+:.*##/ { printf "  $(CYAN)%-15s$(RESET) %s\n", $$1, $$2 }' \
	    $(MAKEFILE_LIST)
	@printf "\n"
