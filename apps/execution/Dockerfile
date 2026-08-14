FROM ghcr.io/astral-sh/uv:python3.14-bookworm AS builder
WORKDIR /app
ENV UV_COMPILE_BYTECODE=1
COPY pyproject.toml uv.lock ./
COPY packages/database ./packages/database
COPY packages/security ./packages/security
COPY packages/deid ./packages/deid
COPY packages/storage ./packages/storage
COPY apps/execution ./apps/execution
RUN --mount=type=cache,target=/root/.cache/uv \
    uv sync --no-dev --package apps-execution

FROM python:3.14-slim-bookworm
WORKDIR /app

RUN groupadd -g 1000 appuser && \
    useradd -r -u 1000 -g appuser appuser

COPY --chown=1000:1000 --from=builder /app/.venv /app/.venv
COPY --chown=1000:1000 packages/database /app/packages/database
COPY --chown=1000:1000 packages/security /app/packages/security
COPY --chown=1000:1000 packages/deid /app/packages/deid
COPY --chown=1000:1000 packages/storage /app/packages/storage
COPY --chown=1000:1000 apps/execution /app/apps/execution
COPY --chown=1000:1000 scripts/start.py /app/scripts/start.py

RUN chown -R 1000:1000 /app

ENV PATH="/app/.venv/bin:$PATH"
EXPOSE 8000

USER 1000

CMD ["python", "scripts/start.py", "execution", "--host", "0.0.0.0", "--port", "8000"]
