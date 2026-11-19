FROM r-base:4.6.2@sha256:7bb56b8b7c849d1cea9a5cff981789622340326abdce817ed44c00ac45947a37

LABEL maintainer="Cadence Clinical Platform Team <dev@cadence.clinical>"
LABEL description="GxP-validated statistical computing execution base image for R"
LABEL version="2026.1"

RUN apt-get update && apt-get install -y --no-install-recommends \
    libcurl4-openssl-dev \
    libssl-dev \
    libxml2-dev \
    && rm -rf /var/lib/apt/lists/*

RUN groupadd --system --gid 10001 sce \
    && useradd --system --uid 10001 --gid sce --create-home --home-dir /home/sce --shell /usr/sbin/nologin sce \
    && mkdir -p /workspace \
    && chown -R sce:sce /workspace

WORKDIR /workspace
USER sce
CMD ["Rscript"]
