FROM ghcr.io/astral-sh/uv:python3.14-bookworm

WORKDIR /app

# Copy the dependency files
COPY pyproject.toml ./

# We use uv sync to install the packages outside the mounted /app
ENV UV_PROJECT_ENVIRONMENT=/opt/.venv
RUN --mount=type=cache,target=/root/.cache/uv \
    uv sync --all-extras --no-install-project

# Ensure we use the virtual environment by default
ENV PATH="/opt/.venv/bin:$PATH"
ENV PYTHONPATH="/app"
ENV AUDIT_LOG_SECRET_KEY=""
ENV GATEWAY_SECRET=""
ENV SIGNING_SECRET=""
ENV INBOUND_EMAIL_HMAC_SECRET=""

CMD ["python", "--version"]
