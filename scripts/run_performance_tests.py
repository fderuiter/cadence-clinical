import csv
import os
import subprocess
import time

import httpx

PORT_DESIGNER = 8001
PORT_EXECUTION = 8002
SLA_THRESHOLD_MS = 1000.0


def wait_for_server(port, timeout=45):
    """Poll the health check endpoint until responsive or timeout is reached."""
    url = f"http://localhost:{port}/health"
    start_time = time.time()
    while time.time() - start_time < timeout:
        try:
            response = httpx.get(url, timeout=2.0)
            if response.status_code == 200:
                print(f"[Runner] Server on port {port} is responsive and healthy.")
                return True
        except Exception:
            pass
        time.sleep(1)
    return False


def run_performance_tests():
    # 1. Database setup & environment config
    db_file = "cadence_perf.db"
    db_path = os.path.abspath(db_file)
    if os.path.exists(db_path):
        try:
            os.remove(db_path)
            print(f"[Runner] Deleted existing performance DB: {db_path}")
        except Exception as e:
            print(f"[Runner] Warning: could not delete {db_path}: {e}")

    # Set up database URL
    db_url = os.environ.get("DATABASE_URL")
    if not db_url:
        db_url = f"sqlite+aiosqlite:///{db_path}"
        print(
            f"[Runner] No DATABASE_URL specified. Defaulting to isolated SQLite: {db_url}"
        )
    else:
        print(f"[Runner] Using database URL from environment: {db_url}")

    # Pre-create database tables
    try:
        print("[Runner] Pre-creating database tables...")
        # Inject core-models into sys.path to allow imports to work
        import sys

        core_models_path = os.path.abspath(
            os.path.join(os.path.dirname(__file__), "..", "packages", "core-models")
        )
        if core_models_path not in sys.path:
            sys.path.insert(0, core_models_path)

        import asyncio

        from sqlalchemy import text

        from apps.execution.database.core import db_manager
        from apps.execution.database.models import Base

        async def create_tables():
            db_manager.init_db(db_url)
            async with db_manager.engine.begin() as conn:
                if "postgres" in db_url or "postgresql" in db_url:
                    await conn.execute(
                        text("CREATE SCHEMA IF NOT EXISTS audit_schema;")
                    )
                await conn.run_sync(Base.metadata.create_all)
            await db_manager.close()

        asyncio.run(create_tables())
        print("[Runner] Database tables initialized successfully.")
    except Exception as e:
        print(f"[Runner] Warning: could not pre-create database tables: {e}")

    env = os.environ.copy()
    env["PYTHONPATH"] = (
        f"/app:/app/packages/core-models:{env.get('PYTHONPATH', '')}".strip(":")
    )
    env["DATABASE_URL"] = db_url
    env["TEST_DATABASE_URL"] = db_url
    env["TERMINOLOGY_OFFLINE"] = "true"
    env["GATEWAY_SECRET"] = "internal-gateway-secret-12345"

    # Ensure reports directory exists
    reports_dir = os.path.abspath("perf_reports")
    os.makedirs(reports_dir, exist_ok=True)

    processes = []
    try:
        # 2. Launch servers in the background
        print("[Runner] Starting Designer service on port 8001...")
        p_designer = subprocess.Popen(
            [
                "uv",
                "run",
                "uvicorn",
                "apps.designer.main:app",
                "--port",
                str(PORT_DESIGNER),
            ],
            env=env,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
        )
        processes.append(p_designer)

        print("[Runner] Starting Execution service on port 8002...")
        p_execution = subprocess.Popen(
            [
                "uv",
                "run",
                "uvicorn",
                "apps.execution.main:app",
                "--port",
                str(PORT_EXECUTION),
            ],
            env=env,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
        )
        processes.append(p_execution)

        # 3. Poll servers until responsive
        print("[Runner] Polling background servers to ensure they are responsive...")
        if not wait_for_server(PORT_DESIGNER) or not wait_for_server(PORT_EXECUTION):
            print(
                "[Runner] Error: One or both servers failed to start or respond within timeout."
            )
            # Print stdout/stderr logs of background servers to help debugging
            for name, p in [("Designer", p_designer), ("Execution", p_execution)]:
                print(f"\n--- {name} Server stdout/stderr ---")
                try:
                    p.terminate()
                    out, err = p.communicate(timeout=2)
                    print(f"STDOUT:\n{out}\nSTDERR:\n{err}")
                except Exception as e:
                    print(f"Error communicating with {name}: {e}")
            sys.exit(1)

        # 4. Launch Locust load test in headless mode
        print("[Runner] Launching Locust load test suite...")
        locust_cmd = [
            "uv",
            "run",
            "locust",
            "-f",
            "tests/performance/locustfile.py",
            "--headless",
            "-u",
            "10",
            "-r",
            "2",
            "--run-time",
            "30s",
            "--html",
            os.path.join(reports_dir, "report.html"),
            "--csv",
            os.path.join(reports_dir, "report"),
        ]

        result = subprocess.run(locust_cmd, capture_output=True, text=True)
        print("[Runner] Locust execution complete.")
        print(result.stdout)
        if result.returncode != 0:
            print("[Runner] Locust error output:")
            print(result.stderr)

        # 5. Parse results CSV to enforce the 1000ms latency SLA
        stats_csv = os.path.join(reports_dir, "report_stats.csv")
        if not os.path.exists(stats_csv):
            print(f"[Runner] Error: Stats CSV file not found at {stats_csv}")
            sys.exit(1)

        print("\n=== Performance Metrics Summary ===")
        sla_violated = False
        with open(stats_csv, mode="r", encoding="utf-8") as f:
            reader = csv.DictReader(f)
            for row in reader:
                name = row.get("Name")
                method = row.get("Type")

                # Robustly find average response time column
                avg_latency_str = None
                for key, val in row.items():
                    if not key:
                        continue
                    key_lower = key.lower().replace("_", " ").replace("-", " ")
                    if (
                        "average response time" in key_lower
                        or "avg response time" in key_lower
                        or "average latency" in key_lower
                    ):
                        avg_latency_str = val
                        break

                if not avg_latency_str:
                    continue

                # Skip the aggregate Total / Aggregated rows
                if name in ("Aggregated", "Total") or not method:
                    continue

                try:
                    avg_latency = float(avg_latency_str)
                except (TypeError, ValueError):
                    continue

                print(f"{method:4} {name:60} : Average Latency = {avg_latency:.2f}ms")
                if avg_latency > SLA_THRESHOLD_MS:
                    print(
                        f"      [SLA VIOLATION] Average latency exceeds {SLA_THRESHOLD_MS}ms SLA limit!"
                    )
                    sla_violated = True

        print("===================================\n")

        if sla_violated:
            print(
                "[Runner] Build failed: Core API endpoint(s) violated the 1000ms average latency SLA."
            )
            sys.exit(1)
        else:
            print(
                "[Runner] Success: All core API endpoints are within the 1000ms latency SLA."
            )
            sys.exit(0)

    finally:
        # 6. Teardown background processes cleanly
        print("[Runner] Cleaning up and terminating background servers...")
        for p in processes:
            try:
                p.terminate()
                p.wait(timeout=5)
            except subprocess.TimeoutExpired:
                print("[Runner] Server process did not terminate cleanly; killing...")
                p.kill()
            except Exception as e:
                print(f"[Runner] Error terminating server: {e}")

        # Clean up database file
        if os.path.exists(db_path):
            try:
                os.remove(db_path)
                print("[Runner] Cleaned up isolated database.")
            except Exception as e:
                print(f"[Runner] Error deleting database file: {e}")


if __name__ == "__main__":
    run_performance_tests()
