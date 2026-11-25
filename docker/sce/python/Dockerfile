FROM python:3.14-slim@sha256:9aaa474097d094040649f2162a627c0049b408f52138d980916788d80f04802f

LABEL maintainer="Cadence Clinical Platform Team <dev@cadence.clinical>"
LABEL description="GxP-validated statistical computing execution base image for Python"
LABEL version="2026.1"

RUN apt-get update \
    && apt-get install -y --only-upgrade --no-install-recommends \
        libzstd1=1.5.7+dfsg-1+deb13u1 \
        zlib1g=1:1.3.dfsg+really1.3.1-1+deb13u1 \
    && apt-get install -y --no-install-recommends \
        build-essential=12.12 \
        libgomp1=14.2.0-19 \
    && rm -rf /var/lib/apt/lists/*

RUN pip install --no-cache-dir --upgrade \
    pip==26.3 \
    setuptools==84.2.1 \
    urllib3==2.8.0 \
    msgpack==1.2.4 \
    && pip install --no-cache-dir \
    pandas==3.1.1 \
    numpy==2.5.5 \
    scipy==1.18.1 \
    polars==2.1.0 \
    scikit-learn==1.9.1 \
    statsmodels==0.15.0 \
    matplotlib==3.11.2 \
    seaborn==0.13.2 \
    && pip uninstall -y pip

RUN groupadd --system --gid 10001 sce \
    && useradd --system --uid 10001 --gid sce --create-home --home-dir /home/sce --shell /usr/sbin/nologin sce \
    && mkdir -p /workspace \
    && chown -R sce:sce /workspace

ENV MPLCONFIGDIR=/tmp/matplotlib
WORKDIR /workspace
USER sce
CMD ["python3"]
