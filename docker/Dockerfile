# ==============================================================================
# Cadence Clinical Research Software — Central Shared Base Runtime Image
# ==============================================================================

ARG PYTHON_VERSION=3.14
ARG UV_IMAGE=ghcr.io/astral-sh/uv:python3.14-bookworm
ARG RUNTIME_IMAGE=python:3.14-slim-bookworm

# ------------------------------------------------------------------------------
# 1. Base Runtime Stage with Native C-Libraries for PDF Rendering
# ------------------------------------------------------------------------------
FROM ${RUNTIME_IMAGE} AS pdf-base

# Install native C-libraries for PDF rendering (WeasyPrint / Pango / Cairo / HarfBuzz)
RUN apt-get update && apt-get install -y --no-install-recommends \
    libpango-1.0-0 \
    libpangoft2-1.0-0 \
    libcairo2 \
    libharfbuzz0b \
    && rm -rf /var/lib/apt/lists/*

# Create standard non-root application user
RUN groupadd -g 1000 appuser && \
    useradd -r -u 1000 -g appuser appuser

# ------------------------------------------------------------------------------
# 2. Builder Stage leveraging Astral uv
# ------------------------------------------------------------------------------
FROM ${UV_IMAGE} AS builder
WORKDIR /app
ENV UV_COMPILE_BYTECODE=1

COPY pyproject.toml uv.lock ./
COPY packages ./packages

RUN --mount=type=cache,target=/root/.cache/uv \
    uv sync --all-extras --no-install-project

# ------------------------------------------------------------------------------
# 3. Production Runtime Stage (Enforce non-root USER 1000 / appuser execution)
# ------------------------------------------------------------------------------
FROM pdf-base AS runtime
WORKDIR /app

COPY --chown=1000:1000 --from=builder /opt/.venv /opt/.venv
COPY --chown=1000:1000 . /app

RUN chown -R appuser:appuser /app

ENV PATH="/opt/.venv/bin:$PATH"
ENV PYTHONPATH="/app"
ENV AUDIT_LOG_SECRET_KEY=""
ENV GATEWAY_SECRET=""
ENV SIGNING_SECRET=""
ENV INBOUND_EMAIL_HMAC_SECRET=""

USER 1000

CMD ["python", "--version"]
