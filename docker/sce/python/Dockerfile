FROM python:3.15-slim@sha256:65a65fc7b30b06cb6df4930fd4650697cb23f4c2698ab81fc87e4b031bc36928

LABEL maintainer="Cadence Clinical Platform Team <dev@cadence.clinical>"
LABEL description="GxP-validated statistical computing execution base image for Python"
LABEL version="2026.1"

RUN apt-get update \
    && apt-get install -y --only-upgrade --no-install-recommends zlib1g \
    && apt-get install -y --no-install-recommends \
        build-essential \
        libgomp1 \
    && rm -rf /var/lib/apt/lists/*

RUN pip install --no-cache-dir --upgrade \
    pip \
    setuptools \
    urllib3 \
    msgpack \
    && pip install --no-cache-dir \
    pandas \
    numpy \
    scipy \
    polars \
    scikit-learn \
    statsmodels \
    matplotlib \
    seaborn \
    && pip uninstall -y pip

RUN groupadd --system --gid 10001 sce \
    && useradd --system --uid 10001 --gid sce --create-home --home-dir /home/sce --shell /usr/sbin/nologin sce \
    && mkdir -p /workspace \
    && chown -R sce:sce /workspace

ENV MPLCONFIGDIR=/tmp/matplotlib
WORKDIR /workspace
USER sce
CMD ["python3"]
