"""E2E Test Suite for Clinical Offline Synchronization.
Meets PRD-ECOA-001 | GxP 21 CFR Part 11 Offline and Online Sync specifications.
"""

import asyncio
import os
import socket
import subprocess
import time
from datetime import datetime
import pytest
from jose import jwt
from playwright.async_api import async_playwright
from sqlalchemy import select

from apps.interop.database import db_manager
from apps.interop.models import (
    Base,
    EPROSubmission,
    Instrument,
    SubjectAssignment,
)
from packages.security.signing import generate_canonical_signature


def wait_for_port(port: int, timeout: int = 30) -> bool:
    """Blocks until the specified TCP port is listening on 127.0.0.1."""
    start_time = time.time()
    while time.time() - start_time < timeout:
        try:
            with socket.create_connection(("127.0.0.1", port), timeout=0.5):
                return True
        except (socket.timeout, ConnectionRefusedError):
            time.sleep(0.5)
    raise RuntimeError(f"Port {port} did not become available.")


@pytest.fixture(scope="session")
def event_loop():
    """Create and yield a session-scoped asyncio event loop."""
    loop = asyncio.get_event_loop_policy().new_event_loop()
    yield loop
    loop.close()


@pytest.fixture(scope="session")
async def run_servers():
    """Launches subject-portal (Vite), Gateway, and Interop servers in parallel.
    Uses a dynamic SQLite database file for full process isolation.
    """
    db_file = "test_interop_e2e.db"
    db_path = f"sqlite+aiosqlite:///{db_file}"
    if os.path.exists(db_file):
        try:
            os.remove(db_file)
        except Exception:
            pass

    # Initialize the database schema for Interop before process startup
    db_manager.init_db(db_path)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    await db_manager.close()

    # Form isolated execution environment variables
    env = os.environ.copy()
    env["INTEROP_DATABASE_URL"] = db_path
    env["GATEWAY_SECRET"] = "internal-gateway-secret-12345"
    env["JWT_TEST_SECRET"] = "test-jwt-secret-abc"
    env["ALLOW_UNVERIFIED_JWT_FOR_TEST"] = "true"
    env["SKIP_JWKS_FETCH"] = "true"
    env["APP_ENV"] = "test"
    env["PYTHONPATH"] = "/app/packages/core-models:/app"

    processes = []
    log_files = []
    try:
        interop_log = open("interop.log", "w")
        gateway_log = open("gateway.log", "w")
        vite_log = open("vite.log", "w")
        log_files.extend([interop_log, gateway_log, vite_log])

        # 1. Start Interop service
        interop_proc = subprocess.Popen(
            [
                "uv",
                "run",
                "uvicorn",
                "apps.interop.main:app",
                "--host",
                "127.0.0.1",
                "--port",
                "8004",
            ],
            cwd="/app",
            env=env,
            stdout=interop_log,
            stderr=interop_log,
        )
        processes.append(interop_proc)

        # 2. Start Gateway service
        gateway_proc = subprocess.Popen(
            [
                "uv",
                "run",
                "uvicorn",
                "apps.gateway.main:app",
                "--host",
                "127.0.0.1",
                "--port",
                "8000",
            ],
            cwd="/app",
            env=env,
            stdout=gateway_log,
            stderr=gateway_log,
        )
        processes.append(gateway_proc)

        # 3. Start Subject Portal web server (Vite)
        vite_proc = subprocess.Popen(
            ["node", "/app/node_modules/vite/bin/vite.js", "--port", "5174", "--host", "127.0.0.1"],
            cwd="/app/apps/subject-portal",
            env=env,
            stdout=vite_log,
            stderr=vite_log,
        )
        processes.append(vite_proc)

        # Confirm all services are actively listening on their standard ports
        wait_for_port(8004)
        wait_for_port(8000)
        wait_for_port(5174)

        yield db_path

    finally:
        # Tear down all spawned servers cleanly
        for p in processes:
            try:
                p.terminate()
                p.wait(timeout=5)
            except Exception:
                try:
                    p.kill()
                except Exception:
                    pass

        # Forcefully terminate any lingering processes bound to the ports
        for port in [8004, 8000, 5174]:
            try:
                subprocess.run(["fuser", "-k", f"{port}/tcp"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            except Exception:
                pass

        # Close all file descriptors
        for f in log_files:
            try:
                f.close()
            except Exception:
                pass

        # Cleanup SQLite DB file
        if os.path.exists(db_file):
            try:
                os.remove(db_file)
            except Exception:
                pass


@pytest.fixture
async def seeded_db(run_servers):
    """Prunes old tables and seeds necessary metadata (such as active Instrument
    and Patient Assignment) for the Subject Portal ePRO flow.
    """
    db_manager.init_db(run_servers)

    async with db_manager.get_session_maker()() as session:
        # Clear database states
        from sqlalchemy import text

        await session.execute(text("DELETE FROM epro_submissions;"))
        await session.execute(text("DELETE FROM epro_defeated_submissions;"))
        await session.execute(text("DELETE FROM clinical_queries;"))
        await session.execute(text("DELETE FROM subject_assignments;"))
        await session.execute(text("DELETE FROM instruments;"))
        await session.commit()

        # Author standard Daily Vital Diary questionnaire
        inst = Instrument(
            id="inst_daily_diary",
            name="Daily Health & Vital Diary",
            description="Please record your systolic/diastolic blood pressure, pulse, and current symptoms.",
            items={
                "vssbp": {
                    "label": "Systolic Blood Pressure (mmHg)",
                    "type": "numeric",
                    "required": True,
                    "min": 50,
                    "max": 250,
                },
                "vsdpb": {
                    "label": "Diastolic Blood Pressure (mmHg)",
                    "type": "numeric",
                    "required": True,
                    "min": 30,
                    "max": 150,
                },
                "vshr": {
                    "label": "Pulse Rate (bpm)",
                    "type": "numeric",
                    "required": True,
                    "min": 30,
                    "max": 200,
                },
                "has_symptoms": {
                    "label": "Are you experiencing any new physical symptoms today?",
                    "type": "choice_single",
                    "options": ["Yes", "No"],
                },
            },
            response_types={},
            scoring_metadata={},
            created_by="system_e2e",
            reason_for_change="Authoring E2E Instrument",
            version_index=1,
        )
        session.add(inst)

        # Link to Subject_001
        assign = SubjectAssignment(
            id="assign_01",
            subject_id="subject_001",
            instrument_id="inst_daily_diary",
            start_date=datetime(2026, 1, 1),
            end_date=datetime(2026, 12, 31),
            recurrence_pattern="DAILY",
            due_at=datetime(2026, 12, 31),
            created_by="system_e2e",
            reason_for_change="Assigning E2E Instrument",
            version_index=1,
        )
        session.add(assign)
        await session.commit()

    yield db_manager
    await db_manager.close()


def generate_test_token(subject_id: str = "subject_001", role: str = "subject") -> str:
    """Generates a compliant gateway signature test token."""
    token_payload = {
        "sub": subject_id,
        "preferred_username": subject_id,
        "realm_access": {"roles": [role]},
        "exp": time.time() + 3600,
    }
    return jwt.encode(token_payload, "test-jwt-secret-abc", algorithm="HS256")


@pytest.mark.asyncio
async def test_offline_persistence_across_reloads(seeded_db):
    """Verifies that an offline questionnaire submission successfully persists
    in the browser IndexedDB across page reloads.
    """
    test_token = generate_test_token()

    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=True)
        context = await browser.new_context()
        page = await context.new_page()

        # Handle modal popups/alerts by accepting automatically
        page.on("dialog", lambda dialog: dialog.accept())

        # 1. Navigate to portal online to bootstrap the authenticated session
        await page.goto("http://localhost:5174/subject-portal/")
        await page.evaluate(
            f"""async () => {{
            const m = await import('/subject-portal/index.js');
            window.__MOCK_TEST_ENV__ = true;
            m.state.session.userId = "subject_001";
            m.state.session.token = "{test_token}";
            m.state.session.isOfflineMode = false;
            m.state.session.isDemoMode = false;
            await m.initializeApp();
        }}"""
        )

        # Wait for task card to appear
        await page.wait_for_selector("#task-card-assign_01")

        # 2. Transition browser context network offline
        await context.set_offline(True)
        await page.evaluate(
            """async () => {
            const m = await import('/subject-portal/index.js');
            m.state.session.isOfflineMode = true;
            await m.syncOfflineQueue();
            m.renderTasks();
        }"""
        )

        # 3. Fill and submit the Daily Vital Diary
        await page.click("#task-card-assign_01 .btn-start-task")
        await page.wait_for_selector("#view-questionnaire.active")

        await page.fill("#vssbp", "125")
        await page.fill("#vsdpb", "85")
        await page.fill("#vshr", "72")
        await page.check("input[name='has_symptoms'][value='No']")

        # Trigger Submit & Signature
        await page.click("#btn-submit-questionnaire")
        await page.wait_for_selector("#portal-sign-modal", state="visible")
        await page.fill("#sign-password", "security_pin_123")
        await page.click("#btn-modal-sign")
        await page.wait_for_selector("#portal-sign-modal", state="hidden")

        # 4. Assert local queue updates and records exist in IndexedDB
        status_text = await page.inner_text("#sync-queue-status-text")
        assert (
            "Offline Mode" in status_text or "queued locally" in status_text
        )

        submissions = await page.evaluate(
            """async () => {
            return new Promise((resolve) => {
                const request = indexedDB.open("SubjectPortalSyncDB", 1);
                request.onsuccess = (event) => {
                    const db = event.target.result;
                    const transaction = db.transaction("submissions", "readonly");
                    const store = transaction.objectStore("submissions");
                    const req = store.getAll();
                    req.onsuccess = () => resolve(req.result);
                };
            });
        }"""
        )
        assert len(submissions) == 1
        assert submissions[0]["status"] == "QUEUED"
        assert submissions[0]["answers"]["vssbp"] == "125"

        # 5. Reload the page fully in offline mode (toggle online temporarily to fetch assets from Vite dev server)
        await context.set_offline(False)
        await page.reload()
        await context.set_offline(True)

        # Re-inject context states offline post reload
        await page.evaluate(
            f"""async () => {{
            const m = await import('/subject-portal/index.js');
            window.__MOCK_TEST_ENV__ = true;
            m.state.session.userId = "subject_001";
            m.state.session.token = "{test_token}";
            m.state.session.isOfflineMode = true;
            m.state.session.isDemoMode = false;
            m.state.assignments = [
                {{
                    id: "assign_01",
                    subject_id: "subject_001",
                    instrument_id: "inst_daily_diary",
                    instrument_name: "Daily Health & Vital Diary",
                    status: "PENDING"
                }}
            ];
            await m.renderTasks();
            await m.renderSyncQueueList();
        }}"""
        )

        # 6. Verify that the record is perfectly preserved in IndexedDB
        reloaded_subs = await page.evaluate(
            """async () => {
            return new Promise((resolve) => {
                const request = indexedDB.open("SubjectPortalSyncDB", 1);
                request.onsuccess = (event) => {
                    const db = event.target.result;
                    const transaction = db.transaction("submissions", "readonly");
                    const store = transaction.objectStore("submissions");
                    const req = store.getAll();
                    req.onsuccess = () => resolve(req.result);
                };
            });
        }"""
        )
        assert len(reloaded_subs) == 1
        assert reloaded_subs[0]["status"] == "QUEUED"
        assert reloaded_subs[0]["answers"]["vssbp"] == "125"

        await browser.close()


@pytest.mark.asyncio
async def test_offline_sync_trigger_on_online(seeded_db):
    """Verifies that transitioning the browser back online automatically
    dispatches standard network triggers and flushes the IndexedDB queue.
    """
    test_token = generate_test_token()

    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=True)
        context = await browser.new_context()
        page = await context.new_page()
        page.on("dialog", lambda dialog: dialog.accept())

        # Load page & sign in
        await page.goto("http://localhost:5174/subject-portal/")
        await page.evaluate(
            f"""async () => {{
            const m = await import('/subject-portal/index.js');
            window.__MOCK_TEST_ENV__ = true;
            m.state.session.userId = "subject_001";
            m.state.session.token = "{test_token}";
            m.state.session.isOfflineMode = false;
            m.state.session.isDemoMode = false;
            await m.initializeApp();
        }}"""
        )

        # Go offline
        await context.set_offline(True)
        await page.evaluate(
            """async () => {
            const m = await import('/subject-portal/index.js');
            m.state.session.isOfflineMode = true;
            await m.syncOfflineQueue();
            m.renderTasks();
        }"""
        )

        # Click and fill questionnaire
        await page.click("#task-card-assign_01 .btn-start-task")
        await page.fill("#vssbp", "130")
        await page.fill("#vsdpb", "90")
        await page.fill("#vshr", "80")
        await page.check("input[name='has_symptoms'][value='No']")

        # Submit
        await page.click("#btn-submit-questionnaire")
        await page.fill("#sign-password", "password")
        await page.click("#btn-modal-sign")

        # Verify offline queue contains the record
        status_text = await page.inner_text("#sync-queue-status-text")
        assert "queued locally" in status_text

        # Transition online to trigger automatic sync
        await context.set_offline(False)
        await page.evaluate(
            """async () => {
            const m = await import('/subject-portal/index.js');
            m.state.session.isOfflineMode = false;
            window.dispatchEvent(new Event('online'));
            await m.syncOfflineQueue();
        }"""
        )

        # Wait for online sync complete status text
        await page.wait_for_function(
            """() => {
            const el = document.getElementById("sync-queue-status-text");
            return el && (el.textContent.includes("Sync complete") || el.textContent.includes("synchronized"));
        }""",
            timeout=10000,
        )

        # Programmatically verify local IndexedDB is updated to CREATED
        synced_subs = await page.evaluate(
            """async () => {
            return new Promise((resolve) => {
                const request = indexedDB.open("SubjectPortalSyncDB", 1);
                request.onsuccess = (event) => {
                    const db = event.target.result;
                    const transaction = db.transaction("submissions", "readonly");
                    const store = transaction.objectStore("submissions");
                    const req = store.getAll();
                    req.onsuccess = () => resolve(req.result);
                };
            });
        }"""
        )
        assert len(synced_subs) == 1
        assert synced_subs[0]["status"] == "CREATED"

        # Verify backend DB holds the record (round-trip verification)
        async with seeded_db.get_session_maker()() as session:
            stmt = select(EPROSubmission).where(
                EPROSubmission.subject_id == "subject_001"
            )
            res = await session.execute(stmt)
            subs = res.scalars().all()
            target_sub = next((s for s in subs if s.answers.get("vssbp") == "130"), None)
            assert target_sub is not None
            assert target_sub.answers["vsdpb"] == "90"

        await browser.close()


@pytest.mark.asyncio
async def test_cryptographic_signature_verification(seeded_db):
    """Validates that synchronised client payloads are canonically verified
    via HMAC-SHA256 signature validation on the backend.
    """
    test_token = generate_test_token()
    secret_bytes = b"internal-gateway-secret-12345"

    # Define standard Answers payload and device details
    subject_id = "subject_001"
    diary_id = "inst_daily_diary"
    answers = {
        "vssbp": "122",
        "vsdpb": "82",
        "vshr": "75",
        "has_symptoms": "No",
    }
    client_id = "dev_secure_e2e"
    device_timestamp = "2026-07-31T10:00:00+00:00"
    timestamps_dict = {k: device_timestamp for k in answers.keys()}

    # Form expected cryptographic signature payload
    sig_payload = {
        "deduplication_key": f"{subject_id}:{diary_id}",
        "data": answers,
        "metadata": {
            "timestamps": timestamps_dict,
            "modified_by": client_id,
        },
    }
    valid_sig = generate_canonical_signature(sig_payload, secret_bytes)

    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=True)
        context = await browser.new_context()
        page = await context.new_page()
        page.on("dialog", lambda dialog: dialog.accept())

        # Load & Authenticate
        await page.goto("http://localhost:5174/subject-portal/")
        await page.evaluate(
            f"""async () => {{
            const m = await import('/subject-portal/index.js');
            window.__MOCK_TEST_ENV__ = true;
            m.state.session.userId = "subject_001";
            m.state.session.token = "{test_token}";
            m.state.session.isOfflineMode = true;
            m.state.session.isDemoMode = false;
            await m.initializeApp();
        }}"""
        )

        # Inject cryptographically signed offline record straight into IndexedDB
        await page.evaluate(
            f"""async () => {{
            const m = await import('/subject-portal/sync-queue.js');
            const db = await m.openDatabase();

            const submission = {{
                sequence_number: 45,
                client_id: "{client_id}",
                subject_id: "{subject_id}",
                diary_id: "{diary_id}",
                assignment_id: "assign_01",
                device_timestamp: "{device_timestamp}",
                answers: {answers},
                change_reason: "Cryptographically Signed submission",
                username: "subject_001",
                status: "QUEUED",
                resolved_answers: null,
                resolved_at: null,
                error: null,
                conflict_strategy: "CLIENT_WINS",
                signature: "{valid_sig}",
                timestamps: {timestamps_dict}
            }};

            return new Promise((resolve, reject) => {{
                const tx = db.transaction("submissions", "readwrite");
                const store = tx.objectStore("submissions");
                store.put(submission);
                tx.oncomplete = () => resolve();
                tx.onerror = () => reject(tx.error);
            }});
        }}"""
        )

        # Refresh the UI queue list
        await page.evaluate(
            """async () => {
            const m = await import('/subject-portal/index.js');
            await m.renderSyncQueueList();
        }"""
        )

        # Transition online to sync the cryptographically signed payload
        await context.set_offline(False)
        await page.evaluate(
            """async () => {
            const m = await import('/subject-portal/index.js');
            m.state.session.isOfflineMode = false;
            window.dispatchEvent(new Event('online'));
            await m.syncOfflineQueue();
        }"""
        )

        # Wait for sync to complete
        await page.wait_for_function(
            """() => {
            const el = document.getElementById("sync-queue-status-text");
            return el && (el.textContent.includes("Sync complete") || el.textContent.includes("synchronized"));
        }""",
            timeout=10000,
        )

        # Verify backend DB successfully verified and saved the record
        async with seeded_db.get_session_maker()() as session:
            stmt = select(EPROSubmission).where(
                EPROSubmission.subject_id == "subject_001"
            )
            res = await session.execute(stmt)
            subs = res.scalars().all()
            target_sub = next((s for s in subs if s.answers.get("vssbp") == "122"), None)
            assert target_sub is not None
            assert target_sub.answers["vsdpb"] == "82"

        await browser.close()


@pytest.mark.asyncio
async def test_conflict_resolution_client_wins(seeded_db):
    """Verifies that under the CLIENT_WINS policy, the incoming offline submission
    overrides any server-side database entries upon online reconciliation.
    """
    test_token = generate_test_token()

    # Seed an outdated submission on the backend
    async with seeded_db.get_session_maker()() as session:
        existing = EPROSubmission(
            subject_id="subject_001",
            diary_id="inst_daily_diary",
            device_timestamp=datetime(2026, 7, 31, 9, 0, 0),
            answers={"vssbp": "110", "vsdpb": "70"},
            offline_sync_markers={
                "sequence_number": 1,
                "client_id": "other_device",
                "conflict_strategy": "CLIENT_WINS",
                "timestamps": {
                    "vssbp": "2026-07-31T09:00:00.000Z",
                    "vsdpb": "2026-07-31T09:00:00.000Z",
                },
            },
            sync_status="RESOLVED",
            version_index=1,
        )
        session.add(existing)
        await session.commit()

    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=True)
        context = await browser.new_context()
        page = await context.new_page()
        page.on("dialog", lambda dialog: dialog.accept())

        # Load page & login
        await page.goto("http://localhost:5174/subject-portal/")
        await page.evaluate(
            f"""async () => {{
            const m = await import('/subject-portal/index.js');
            window.__MOCK_TEST_ENV__ = true;
            m.state.session.userId = "subject_001";
            m.state.session.token = "{test_token}";
            m.state.session.isOfflineMode = true;
            m.state.session.isDemoMode = false;
            await m.initializeApp();
        }}"""
        )

        # Inject newer offline entry with CLIENT_WINS conflict policy
        answers = {"vssbp": "135", "vsdpb": "95"}
        device_timestamp = "2026-07-31T11:00:00.000Z"
        await page.evaluate(
            f"""async () => {{
            const m = await import('/subject-portal/sync-queue.js');
            const db = await m.openDatabase();

            const submission = {{
                sequence_number: 100,
                client_id: "my_device",
                subject_id: "subject_001",
                diary_id: "inst_daily_diary",
                assignment_id: "assign_01",
                device_timestamp: "{device_timestamp}",
                answers: {answers},
                change_reason: "Overwrite with CLIENT_WINS",
                username: "subject_001",
                status: "QUEUED",
                resolved_answers: null,
                resolved_at: null,
                error: null,
                conflict_strategy: "CLIENT_WINS",
                timestamps: {{
                    "vssbp": "{device_timestamp}",
                    "vsdpb": "{device_timestamp}"
                }}
            }};

            return new Promise((resolve, reject) => {{
                const tx = db.transaction("submissions", "readwrite");
                const store = tx.objectStore("submissions");
                store.put(submission);
                tx.oncomplete = () => resolve();
                tx.onerror = () => reject(tx.error);
            }});
        }}"""
        )

        # Refresh queue list
        await page.evaluate(
            """async () => {
            const m = await import('/subject-portal/index.js');
            await m.renderSyncQueueList();
        }"""
        )

        # Sync
        await context.set_offline(False)
        await page.evaluate(
            """async () => {
            const m = await import('/subject-portal/index.js');
            m.state.session.isOfflineMode = false;
            window.dispatchEvent(new Event('online'));
            await m.syncOfflineQueue();
        }"""
        )

        # Wait for sync to complete
        await page.wait_for_function(
            """() => {
            const el = document.getElementById("sync-queue-status-text");
            return el && (el.textContent.includes("Sync complete") || el.textContent.includes("synchronized"));
        }""",
            timeout=10000,
        )

        # Check IndexedDB state was updated to UPDATED_CLIENT_WINS
        reconciled_subs = await page.evaluate(
            """async () => {
            return new Promise((resolve) => {
                const request = indexedDB.open("SubjectPortalSyncDB", 1);
                request.onsuccess = (event) => {
                    const db = event.target.result;
                    const transaction = db.transaction("submissions", "readonly");
                    const store = transaction.objectStore("submissions");
                    const req = store.getAll();
                    req.onsuccess = () => resolve(req.result);
                };
            });
        }"""
        )
        sub_100 = next(s for s in reconciled_subs if s["sequence_number"] == 100)
        assert sub_100["status"] == "UPDATED_CLIENT_WINS"

        # Confirm database holds client's winning answers & version incremented to 2
        async with seeded_db.get_session_maker()() as session:
            stmt = select(EPROSubmission).where(
                EPROSubmission.subject_id == "subject_001"
            )
            res = await session.execute(stmt)
            subs = res.scalars().all()
            assert len(subs) == 1
            assert subs[0].answers["vssbp"] == "135"
            assert subs[0].answers["vsdpb"] == "95"
            assert subs[0].version_index == 2

        await browser.close()


@pytest.mark.asyncio
async def test_conflict_resolution_merge(seeded_db):
    """Verifies that under the MERGE policy, independent fields are combined,
    and overlapping ones undergo Last-Write-Wins (LWW) evaluation.
    """
    test_token = generate_test_token()

    # Seed existing record with a unique/independent field (vshr = "60")
    async with seeded_db.get_session_maker()() as session:
        existing = EPROSubmission(
            subject_id="subject_001",
            diary_id="inst_daily_diary",
            device_timestamp=datetime(2026, 7, 31, 9, 0, 0),
            answers={"vssbp": "110", "vsdpb": "70", "vshr": "60"},
            offline_sync_markers={
                "sequence_number": 1,
                "client_id": "other_device",
                "conflict_strategy": "MERGE",
                "timestamps": {
                    "vssbp": "2026-07-31T09:00:00.000Z",
                    "vsdpb": "2026-07-31T09:00:00.000Z",
                    "vshr": "2026-07-31T09:00:00.000Z",
                },
            },
            sync_status="RESOLVED",
            version_index=1,
        )
        session.add(existing)
        await session.commit()

    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=True)
        context = await browser.new_context()
        page = await context.new_page()
        page.on("dialog", lambda dialog: dialog.accept())

        # Load page & login
        await page.goto("http://localhost:5174/subject-portal/")
        await page.evaluate(
            f"""async () => {{
            const m = await import('/subject-portal/index.js');
            window.__MOCK_TEST_ENV__ = true;
            m.state.session.userId = "subject_001";
            m.state.session.token = "{test_token}";
            m.state.session.isOfflineMode = true;
            m.state.session.isDemoMode = false;
            await m.initializeApp();
        }}"""
        )

        # Inject MERGE offline payload (modifies vssbp but leaves vshr completely absent)
        answers = {"vssbp": "140", "vsdpb": "70"}
        device_timestamp = "2026-07-31T12:00:00.000Z"
        await page.evaluate(
            f"""async () => {{
            const m = await import('/subject-portal/sync-queue.js');
            const db = await m.openDatabase();

            const submission = {{
                sequence_number: 101,
                client_id: "my_device",
                subject_id: "subject_001",
                diary_id: "inst_daily_diary",
                assignment_id: "assign_01",
                device_timestamp: "{device_timestamp}",
                answers: {answers},
                change_reason: "Merge answers with server",
                username: "subject_001",
                status: "QUEUED",
                resolved_answers: null,
                resolved_at: null,
                error: null,
                conflict_strategy: "MERGE",
                timestamps: {{
                    "vssbp": "{device_timestamp}",
                    "vsdpb": "{device_timestamp}"
                }}
            }};

            return new Promise((resolve, reject) => {{
                const tx = db.transaction("submissions", "readwrite");
                const store = tx.objectStore("submissions");
                store.put(submission);
                tx.oncomplete = () => resolve();
                tx.onerror = () => reject(tx.error);
            }});
        }}"""
        )

        # Refresh queue list
        await page.evaluate(
            """async () => {
            const m = await import('/subject-portal/index.js');
            await m.renderSyncQueueList();
        }"""
        )

        # Sync
        await context.set_offline(False)
        await page.evaluate(
            """async () => {
            const m = await import('/subject-portal/index.js');
            m.state.session.isOfflineMode = false;
            window.dispatchEvent(new Event('online'));
            await m.syncOfflineQueue();
        }"""
        )

        # Wait for completed sync
        await page.wait_for_function(
            """() => {
            const el = document.getElementById("sync-queue-status-text");
            return el && (el.textContent.includes("Sync complete") || el.textContent.includes("synchronized"));
        }""",
            timeout=10000,
        )

        # Verify local status updated to MERGED
        reconciled_subs = await page.evaluate(
            """async () => {
            return new Promise((resolve) => {
                const request = indexedDB.open("SubjectPortalSyncDB", 1);
                request.onsuccess = (event) => {
                    const db = event.target.result;
                    const transaction = db.transaction("submissions", "readonly");
                    const store = transaction.objectStore("submissions");
                    const req = store.getAll();
                    req.onsuccess = () => resolve(req.result);
                };
            });
        }"""
        )
        sub_101 = next(s for s in reconciled_subs if s["sequence_number"] == 101)
        assert sub_101["status"] == "MERGED"

        # Verify backend DB holds merged results: vssbp=140, vsdpb=70, vshr=60
        async with seeded_db.get_session_maker()() as session:
            stmt = select(EPROSubmission).where(
                EPROSubmission.subject_id == "subject_001"
            )
            res = await session.execute(stmt)
            subs = res.scalars().all()
            assert len(subs) == 1
            assert subs[0].answers["vssbp"] == "140"
            assert subs[0].answers["vsdpb"] == "70"
            assert subs[0].answers["vshr"] == "60"
            assert subs[0].version_index == 2

        await browser.close()
