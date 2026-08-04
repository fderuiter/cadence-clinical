FROM ghcr.io/astral-sh/uv:python3.14-bookworm AS builder
WORKDIR /app
ENV UV_COMPILE_BYTECODE=1
COPY pyproject.toml uv.lock ./
COPY packages/database ./packages/database
COPY packages/security ./packages/security
COPY apps/econsent ./apps/econsent
RUN --mount=type=cache,target=/root/.cache/uv \
    uv sync --frozen --no-dev --package apps-econsent

FROM python:3.14-slim-bookworm
WORKDIR /app
COPY --from=builder /app/.venv /app/.venv
COPY packages/database /app/packages/database
COPY packages/security /app/packages/security
COPY apps/econsent /app/apps/econsent
ENV PATH="/app/.venv/bin:$PATH"
EXPOSE 8000
CMD ["uvicorn", "apps.econsent.main:app", "--host", "0.0.0.0", "--port", "8000"]
