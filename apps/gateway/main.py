from fastapi import FastAPI

app = FastAPI(title="Cadence Clinical - API Gateway", version="0.1.0")


@app.get("/health")
async def health_check():
    return {"status": "ok", "service": "gateway"}
