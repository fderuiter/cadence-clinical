FROM ghcr.io/astral-sh/uv:python3.14-bookworm AS builder
WORKDIR /app
ENV UV_COMPILE_BYTECODE=1
COPY pyproject.toml uv.lock ./
COPY packages/security ./packages/security
COPY packages/core-models ./packages/core-models
COPY apps/designer ./apps/designer
RUN --mount=type=cache,target=/root/.cache/uv \
    uv sync --no-dev --package apps-designer

FROM python:3.14-slim-bookworm
WORKDIR /app
# WeasyPrint may require additional system libraries for Pango/Cairo, but for a small runtime, slim is the baseline.
COPY --from=builder /app/.venv /app/.venv
COPY packages/security /app/packages/security
COPY packages/core-models /app/packages/core-models
COPY apps/designer /app/apps/designer
ENV PATH="/app/.venv/bin:$PATH"
EXPOSE 8000
CMD ["uvicorn", "apps.designer.main:app", "--host", "0.0.0.0", "--port", "8000"]
