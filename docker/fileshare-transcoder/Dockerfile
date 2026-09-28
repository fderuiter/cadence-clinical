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
COPY apps/fileshare ./apps/fileshare

RUN --mount=type=cache,target=/root/.cache/uv \
    uv sync --no-dev --package apps-fileshare

# ------------------------------------------------------------------------------
# 2. Production Runtime Stage with FFmpeg, LibreOffice, and PDF C-libraries
# ------------------------------------------------------------------------------
FROM ${RUNTIME_IMAGE} AS runtime

# Install FFmpeg, LibreOffice headless, and required audio/video/document rendering packages
RUN apt-get update && apt-get install -y --no-install-recommends \
    ffmpeg \
    libreoffice-writer \
    libreoffice-calc \
    libreoffice-impress \
    default-jre-headless \
    fonts-dejavu \
    libpango-1.0-0 \
    libpangoft2-1.0-0 \
    libcairo2 \
    libharfbuzz0b \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

RUN groupadd -g 1000 appuser && \
    useradd -r -u 1000 -g appuser appuser

COPY --chown=1000:1000 --from=builder /app/.venv /app/.venv
COPY --chown=1000:1000 packages /app/packages
COPY --chown=1000:1000 apps/fileshare /app/apps/fileshare
COPY --chown=1000:1000 scripts /app/scripts

RUN chown -R appuser:appuser /app

ENV PATH="/app/.venv/bin:$PATH"
ENV PYTHONPATH="/app"
ENV AUDIT_LOG_SECRET_KEY=""
ENV GATEWAY_SECRET=""
ENV SIGNING_SECRET=""

USER 1000

CMD ["python", "-c", "import asyncio; from apps.fileshare.workers.transcode_worker import run_transcoder_daemon; from apps.fileshare.workers.conversion_worker import run_conversion_daemon; asyncio.run(asyncio.gather(run_transcoder_daemon(), run_conversion_daemon()))"]
