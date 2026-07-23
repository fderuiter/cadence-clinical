import os
import uuid
from typing import Any

import defusedxml.minidom as minidom
from jinja2 import Environment, FileSystemLoader, select_autoescape
from sqlalchemy import select, update

from apps.execution.database.context import current_session
from apps.execution.database.models import TranslationJob

# Setup Jinja2 environment
TEMPLATE_DIR = os.path.join(os.path.dirname(__file__), "templates")
env = Environment(
    loader=FileSystemLoader(TEMPLATE_DIR),
    autoescape=select_autoescape(default_for_string=True, default=True),
)


def extract_appearance(item: dict[str, Any]) -> str | None:
    """Extract grid layout metadata properties into standard Enketo appearance classes.

    Parses USDM item layout properties (`cols`, `column_span`, `span`) directly or from
    a nested layout/grid object, converting width factors into OpenRosa/Enketo classes (`w1`-`w4`).

    Args:
        item (dict[str, Any]): The USDM study item definition dictionary.

    Returns:
        str | None: The computed appearance class, or None if no layout is specified.
    """
    # Check item.cols, item.column_span, item.span
    keys = ["cols", "column_span", "span"]
    for k in keys:
        if k in item:
            val = item[k]
            if str(val) in ["1", "2", "3", "4"]:
                return f"w{val}"

    # Check layout sub-objects
    for sub in ["layout", "grid"]:
        if sub in item and isinstance(item[sub], dict):
            for k in keys:
                if k in item[sub]:
                    val = item[sub][k]
                    if str(val) in ["1", "2", "3", "4"]:
                        return f"w{val}"
    return None


async def process_translation(
    study_id: str, payload: dict[str, Any], session_factory: Any
) -> None:
    """Legacy background worker adapted for the polling daemon model.

    Creates a TranslationJob record in PENDING status and triggers the polling loop.

    Args:
        study_id (str): The unique identifier of the source study.
        payload (dict[str, Any]): The raw USDM protocol payload.
        session_factory (Any): The SQLAlchemy asynchronous session factory.

    Returns:
        None
    """
    async with session_factory() as session:
        async with session.begin():
            token = current_session.set(session)
            try:
                job = TranslationJob(
                    study_id=study_id,
                    status="PENDING",
                    payload=payload,
                )
                session.add(job)
            finally:
                current_session.reset(token)

    # Run the polling worker to process it immediately in the background
    await poll_and_process_jobs()


async def run_translation_job(
    job_id: str, study_id: str, payload: dict[str, Any], session_factory: Any
) -> None:
    """Background worker that translates USDM payload into CDISC ODM and OpenRosa XML layouts.

    Updates the existing TranslationJob record with results or failure information.

    Args:
        job_id (str): The ID of the TranslationJob database record.
        study_id (str): The unique identifier of the source study.
        payload (dict[str, Any]): The raw USDM protocol payload.
        session_factory (Any): The SQLAlchemy asynchronous session factory.

    Returns:
        None
    """
    async with session_factory() as session:
        async with session.begin():
            # Setup the DB session in context so our audit logger can find it
            token = current_session.set(session)

            try:
                # Fetch the existing job
                stmt = select(TranslationJob).where(TranslationJob.id == job_id)
                result = await session.execute(stmt)
                job = result.scalars().first()
                if job is None:
                    return

                # Requirement 6: Validate input structures against schema translation rules
                if not payload or not isinstance(payload, dict):
                    raise ValueError("Payload must be a dictionary.")
                if "protocol" not in payload:
                    raise ValueError(
                        "Validation Failed: 'protocol' missing from study definition."
                    )

                # Process items for templates
                raw_items = payload.get("protocol", {}).get("items", [])
                processed_items = []
                for item in raw_items:
                    item_id = item.get("id")
                    if not item_id:
                        item_id = f"item_{uuid.uuid4().hex[:8]}"

                    item_name = item.get("name", "Unknown Field")
                    item_type = item.get("type", "string")
                    appearance = extract_appearance(item)

                    processed_items.append(
                        {
                            "id": item_id,
                            "name": item_name,
                            "type": item_type,
                            "appearance": appearance,
                        }
                    )

                template_data = {
                    "study_id": study_id,
                    "name": payload.get("name", f"Study {study_id}"),
                    "items": processed_items,
                }

                # Render templates
                odm_template = env.get_template("odm_template.xml.j2")
                odm_xml_str = odm_template.render(**template_data)

                openrosa_template = env.get_template("openrosa_template.xml.j2")
                openrosa_xml_str = openrosa_template.render(**template_data)

                # Format outputs via minidom to guarantee compatibility with existing expectations
                # We strip out whitespace-only text nodes created by jinja templating before formatting
                def pretty_print(xml_string: str) -> str:
                    """Format an XML string with indentation for better readability.

                    Removes whitespace-only text nodes generated by Jinja2 templates before
                    applying standard formatting via minidom to ensure expected line breaks.

                    Args:
                        xml_string (str): The raw XML string to format.

                    Returns:
                        str: The pretty-printed XML string.
                    """
                    dom = minidom.parseString(xml_string)
                    # Remove blank text nodes so toprettyxml doesn't add extra newlines
                    for node in dom.getElementsByTagName("*"):
                        for child in list(node.childNodes):
                            # 3 is the integer value for Node.TEXT_NODE
                            if child.nodeType == 3 and not child.data.strip():
                                node.removeChild(child)
                    return dom.toprettyxml(indent="  ")

                odm_str = pretty_print(odm_xml_str)
                openrosa_str = pretty_print(openrosa_xml_str)

                job.odm_payload = odm_str
                job.openrosa_payload = openrosa_str
                job.status = "COMPLETED"

            except Exception as e:
                # Fetch again or use existing reference if attached
                stmt = select(TranslationJob).where(TranslationJob.id == job_id)
                res = await session.execute(stmt)
                job_ref = res.scalars().first()
                if job_ref:
                    job_ref.status = "FAILED"
                    job_ref.error_message = str(e)
            finally:
                # Update job execution status
                await session.flush()
                current_session.reset(token)


async def poll_and_process_jobs() -> bool:
    """Finds a pending job, locks it, processes it, and returns True if a job was processed, False otherwise.

    This runs tasks in isolated transactions that commit status updates to the database.

    Returns:
        bool: True if a job was picked up and processed, False otherwise.
    """
    from apps.execution.database.core import db_manager

    session_factory = db_manager.get_session_maker()
    if session_factory is None:
        return False

    async with session_factory() as session:
        async with session.begin():
            token = current_session.set(session)
            try:
                # 1. Query for a pending job, locking it if supported
                stmt = select(TranslationJob).where(TranslationJob.status == "PENDING")
                # SQLite doesn't support SELECT ... FOR UPDATE SKIP LOCKED
                if session.bind.dialect.name != "sqlite":
                    stmt = stmt.with_for_update(skip_locked=True)
                stmt = stmt.limit(1)

                result = await session.execute(stmt)
                job = result.scalars().first()

                if job is None:
                    return False

                # 2. Lock the job by transitioning its status to "PROCESSING"
                job.status = "PROCESSING"
                await session.flush()

                job_id = job.id
                study_id = job.study_id
                payload = job.payload
            finally:
                current_session.reset(token)

    # Run the translation task in a separate, isolated transaction
    await run_translation_job(job_id, study_id, payload, session_factory)
    return True


async def polling_daemon_loop() -> None:
    """Background task that periodically queries the database for pending translation jobs and processes them."""
    import asyncio

    try:
        polling_interval = float(os.getenv("TRANSLATION_POLLING_INTERVAL", "5.0"))
    except ValueError:
        polling_interval = 5.0

    while True:
        try:
            # Process jobs until there are no more pending jobs
            processed = True
            while processed:
                processed = await poll_and_process_jobs()
        except asyncio.CancelledError:
            break
        except Exception as e:
            # Fallback to prevent background loop from dying
            print(f"Error in polling daemon: {e}")

        try:
            await asyncio.sleep(polling_interval)
        except asyncio.CancelledError:
            break


async def recover_active_jobs() -> None:
    """Reset any jobs left in "PROCESSING" status back to "PENDING" on startup."""
    from apps.execution.database.core import db_manager

    session_factory = db_manager.get_session_maker()
    if session_factory is None:
        return
    try:
        async with session_factory() as session:
            async with session.begin():
                token = current_session.set(session)
                try:
                    stmt = (
                        update(TranslationJob)
                        .where(TranslationJob.status == "PROCESSING")
                        .values(
                            status="PENDING",
                            error_message="Recovered and reset to PENDING on startup",
                        )
                    )
                    await session.execute(stmt)
                finally:
                    current_session.reset(token)
    except Exception as e:
        print(
            f"Skipping active jobs recovery on startup (database schema may not be created yet): {e}"
        )
