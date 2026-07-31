FROM ghcr.io/astral-sh/uv:python3.12-bookworm

WORKDIR /app

# Copy the dependency files
COPY pyproject.toml ./

# We use uv sync to install the packages outside the mounted /app
ENV UV_PROJECT_ENVIRONMENT=/opt/.venv
RUN uv sync --all-extras --no-install-project

# Ensure we use the virtual environment by default
ENV PATH="/opt/.venv/bin:$PATH"

CMD ["python", "--version"]
