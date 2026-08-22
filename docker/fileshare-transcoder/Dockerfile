FROM ghcr.io/astral-sh/uv:python3.14-bookworm

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

# Copy the dependency files
COPY pyproject.toml ./

# Install python dependencies via uv
ENV UV_PROJECT_ENVIRONMENT=/opt/.venv
RUN --mount=type=cache,target=/root/.cache/uv \
    uv sync --all-extras --no-install-project

# Environment configuration
ENV PATH="/opt/.venv/bin:$PATH"
ENV PYTHONPATH="/app"
ENV AUDIT_LOG_SECRET_KEY=""
ENV GATEWAY_SECRET=""
ENV SIGNING_SECRET=""

CMD ["python", "-c", "import asyncio; from apps.fileshare.workers.transcode_worker import run_transcoder_daemon; from apps.fileshare.workers.conversion_worker import run_conversion_daemon; asyncio.run(asyncio.gather(run_transcoder_daemon(), run_conversion_daemon()))"]
