FROM r-base:4.4.2@sha256:fe9b29520eeb5292d814b0958783c0ddfcdab37402967a3e67307604354f98d7

LABEL maintainer="Cadence Clinical Platform Team <dev@cadence.clinical>"
LABEL description="GxP-validated statistical computing execution base image for R"
LABEL version="2026.1"

RUN apt-get update && apt-get install -y --no-install-recommends \
    libcurl4-openssl-dev=8.23.0-2 \
    libssl-dev=3.6.5-1 \
    libxml2-dev=2.15.4+dfsg-1+b1 \
    && rm -rf /var/lib/apt/lists/*

RUN groupadd --system --gid 10001 sce \
    && useradd --system --uid 10001 --gid sce --create-home --home-dir /home/sce --shell /usr/sbin/nologin sce \
    && mkdir -p /workspace \
    && chown -R sce:sce /workspace

WORKDIR /workspace
USER sce
CMD ["Rscript"]
