FROM python:3.14-slim

LABEL maintainer="Cadence Clinical Platform Team <dev@cadence.clinical>"
LABEL description="GxP-validated statistical computing execution base image for Python"
LABEL version="2026.1"

RUN apt-get update && apt-get install -y --no-install-recommends \
    build-essential \
    libgomp1 \
    && rm -rf /var/lib/apt/lists/*

RUN pip install --no-cache-dir \
    pandas \
    numpy \
    scipy \
    polars \
    scikit-learn \
    statsmodels \
    matplotlib \
    seaborn

WORKDIR /workspace
CMD ["python3"]
