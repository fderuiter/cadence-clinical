FROM python:3.14-slim

WORKDIR /app

COPY . .

RUN pip install uv && uv sync

CMD ["python", "scripts/start.py", "fileshare", "--host", "0.0.0.0", "--port", "8013"]

