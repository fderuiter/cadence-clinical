FROM ghcr.io/astral-sh/uv:python3.14-bookworm AS builder
WORKDIR /app
ENV UV_COMPILE_BYTECODE=1
COPY pyproject.toml uv.lock ./
COPY packages/database ./packages/database
COPY packages/security ./packages/security
COPY apps/ctms ./apps/ctms
RUN --mount=type=cache,target=/root/.cache/uv \
    uv sync --no-dev --package apps-ctms

FROM python:3.14-slim-bookworm
WORKDIR /app

# Install native C-libraries for PDF rendering (WeasyPrint / Pango / Cairo)
RUN apt-get update && apt-get install -y --no-install-recommends \
    libpango-1.0-0 \
    libpangoft2-1.0-0 \
    libcairo2 \
    libharfbuzz0b \
    && rm -rf /var/lib/apt/lists/*

COPY --from=builder /app/.venv /app/.venv

COPY packages/database /app/packages/database
COPY packages/security /app/packages/security
COPY apps/ctms /app/apps/ctms
ENV PATH="/app/.venv/bin:$PATH"
EXPOSE 8000
CMD ["uvicorn", "apps.ctms.main:app", "--host", "0.0.0.0", "--port", "8000"]
