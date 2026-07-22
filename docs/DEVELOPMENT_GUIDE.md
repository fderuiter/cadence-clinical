# Cadence Clinical Platform - Development Guide

Welcome to the Cadence Clinical Platform. This guide is intended to help developers navigate the local development environment and understand the core architectural constraints.

## Architecture & Decisions

Before making significant changes or starting a new feature, please review the historical design choices and the context behind them. 

All past architectural decisions are documented in our **[Architectural Decisions Index](adr/index.md)**. 

When introducing new architectural changes (e.g., adding a new dependency, modifying data models, or adding a new service), you are required to submit an Architecture Decision Record (ADR) following the mandatory format. A template is provided in the `docs/adr/` directory.

## Getting Started

1. Ensure you have Python 3.11+ installed.
2. Install dependencies using `poetry install`.
3. Set up the Docker infrastructure using the configurations in `docker/`.
4. Review the API guidelines and Pydantic models in `packages/core-models/`.

For detailed submission guidelines, please refer to the main repository documentation.
