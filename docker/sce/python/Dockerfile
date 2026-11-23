FROM python:3.14-slim@sha256:9aaa474097d094040649f2162a627c0049b408f52138d980916788d80f04802f

LABEL maintainer="Cadence Clinical Platform Team <dev@cadence.clinical>"
LABEL description="GxP-validated statistical computing execution base image for Python"
LABEL version="2026.1"

RUN apt-get update \
    && apt-get install -y --only-upgrade --no-install-recommends zlib1g \
    && apt-get install -y --no-install-recommends \
        build-essential \
        libgomp1 \
    && rm -rf /var/lib/apt/lists/*

COPY requirements.txt /tmp/requirements.txt

RUN pip install --no-cache-dir --require-hashes --no-deps -r /tmp/requirements.txt \
    && pip uninstall -y pip \
    && rm -f /tmp/requirements.txt

RUN groupadd --system --gid 10001 sce \
    && useradd --system --uid 10001 --gid sce --create-home --home-dir /home/sce --shell /usr/sbin/nologin sce \
    && mkdir -p /workspace \
    && chown -R sce:sce /workspace

ENV MPLCONFIGDIR=/tmp/matplotlib
WORKDIR /workspace
USER sce
CMD ["python3"]
