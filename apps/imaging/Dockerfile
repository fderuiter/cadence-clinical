# Centralized build arguments for Python & Astral uv
ARG PYTHON_VERSION=3.14
ARG UV_IMAGE=ghcr.io/astral-sh/uv:python3.14-bookworm
ARG RUNTIME_IMAGE=python:3.14-slim-bookworm

# ------------------------------------------------------------------------------
# 1. Builder Stage (Astral uv dependency synchronization)
# ------------------------------------------------------------------------------
FROM ${UV_IMAGE} AS builder
WORKDIR /app
ENV UV_COMPILE_BYTECODE=1

COPY pyproject.toml uv.lock ./
COPY packages ./packages
COPY apps/imaging ./apps/imaging

RUN --mount=type=cache,target=/root/.cache/uv \
    uv sync --no-dev --package apps-imaging

# ------------------------------------------------------------------------------
# 2. Shared Base Stage for Native PDF Rendering Libraries
# ------------------------------------------------------------------------------
FROM ${RUNTIME_IMAGE} AS pdf-base
RUN apt-get update && apt-get install -y --no-install-recommends \
    libpango-1.0-0 \
    libpangoft2-1.0-0 \
    libcairo2 \
    libharfbuzz0b \
    && rm -rf /var/lib/apt/lists/*

# ------------------------------------------------------------------------------
# 3. Production Runtime Stage (Enforce non-root USER 1000 / appuser execution)
# ------------------------------------------------------------------------------
FROM pdf-base AS runtime
WORKDIR /app

RUN groupadd -g 1000 appuser && \
    useradd -r -u 1000 -g appuser appuser

COPY --chown=1000:1000 --from=builder /app/.venv /app/.venv
COPY --chown=1000:1000 packages /app/packages
COPY --chown=1000:1000 apps/imaging /app/apps/imaging
COPY --chown=1000:1000 scripts /app/scripts

RUN chown -R appuser:appuser /app

ENV PATH="/app/.venv/bin:$PATH"
ENV PYTHONPATH="/app"
EXPOSE 8000

USER 1000

CMD ["python", "scripts/start.py", "imaging", "--host", "0.0.0.0", "--port", "8000"]
