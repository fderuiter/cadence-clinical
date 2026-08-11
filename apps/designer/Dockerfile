FROM ghcr.io/astral-sh/uv:python3.14-bookworm@sha256:7b57b9f86450f3b0e1e695f269a8342ef62b1660d26829707e7b8c3ff6f827fc AS builder
WORKDIR /app
ENV UV_COMPILE_BYTECODE=1
COPY pyproject.toml uv.lock ./
COPY packages/security ./packages/security
COPY apps/designer ./apps/designer
RUN --mount=type=cache,target=/root/.cache/uv \
    uv sync --no-dev --package apps-designer

FROM python:3.14-slim-bookworm@sha256:4b4c73b64c11451f2f8352613d9cfc82fc600cc647e3a985e54d3cd2db0e2a39
WORKDIR /app

RUN groupadd -g 1000 appuser && \
    useradd -r -u 1000 -g appuser appuser

COPY --chown=1000:1000 --from=builder /app/.venv /app/.venv
COPY --chown=1000:1000 packages/security /app/packages/security
COPY --chown=1000:1000 apps/designer /app/apps/designer
COPY --chown=1000:1000 scripts/start.py /app/scripts/start.py

RUN chown -R 1000:1000 /app

ENV PATH="/app/.venv/bin:$PATH"
EXPOSE 8000

USER 1000

CMD ["python", "scripts/start.py", "designer", "--host", "0.0.0.0", "--port", "8000"]
