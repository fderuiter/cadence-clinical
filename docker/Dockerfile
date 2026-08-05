FROM ghcr.io/astral-sh/uv:python3.14-bookworm@sha256:7b57b9f86450f3b0e1e695f269a8342ef62b1660d26829707e7b8c3ff6f827fc

WORKDIR /app

# Copy the dependency files
COPY pyproject.toml ./

# We use uv sync to install the packages outside the mounted /app
ENV UV_PROJECT_ENVIRONMENT=/opt/.venv
RUN --mount=type=cache,target=/root/.cache/uv \
    uv sync --all-extras --no-install-project

# Ensure we use the virtual environment by default
ENV PATH="/opt/.venv/bin:$PATH"

CMD ["python", "--version"]
