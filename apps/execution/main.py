from fastapi import FastAPI

app = FastAPI(title="Cadence Clinical - EDC Execution Engine", version="0.1.0")


@app.get("/health")
async def health_check():
    return {"status": "ok", "service": "execution"}
