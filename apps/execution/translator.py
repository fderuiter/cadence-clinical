import uuid
import xml.etree.ElementTree as ET
from typing import Any

import defusedxml.minidom as minidom

from apps.execution.database.context import current_session
from apps.execution.database.models import TranslationJob


async def process_translation(
    study_id: str, payload: dict[str, Any], session_factory: Any
) -> None:
    """Background worker that translates USDM payload into CDISC ODM and OpenRosa XML layouts.

    Args:
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

            job = TranslationJob(study_id=study_id, status="PROCESSING")
            session.add(job)
            await session.flush()

            try:
                # Requirement 6: Validate input structures against schema translation rules
                if not payload or not isinstance(payload, dict):
                    raise ValueError("Payload must be a dictionary.")
                if "protocol" not in payload:
                    raise ValueError(
                        "Validation Failed: 'protocol' missing from study definition."
                    )

                # Proceed to translation
                # Requirement 2: CDISC ODM schemas
                odm_root = ET.Element(
                    "ODM",
                    xmlns="http://www.cdisc.org/ns/odm/v1.3",
                    FileOID=f"ODM.{study_id}",
                )
                study = ET.SubElement(odm_root, "Study", OID=f"Study.{study_id}")
                ET.SubElement(study, "GlobalVariables").text = "Cadence Generated Study"
                meta_odm = ET.SubElement(
                    study, "MetaDataVersion", OID="MDV.1", Name="Version 1.0"
                )

                # Requirement 3: OpenRosa XML forms
                ns_attribs = {
                    "xmlns": "http://www.w3.org/1999/xhtml",
                    "xmlns:xf": "http://www.w3.org/2002/xforms",
                    "xmlns:ev": "http://www.w3.org/2001/xml-events",
                }
                openrosa_root = ET.Element("html", attrib=ns_attribs)
                head = ET.SubElement(openrosa_root, "head")
                title = ET.SubElement(head, "title")
                title.text = payload.get("name", f"Study {study_id}")
                model = ET.SubElement(head, "xf:model")
                instance = ET.SubElement(model, "xf:instance")
                data = ET.SubElement(instance, "data", id=study_id)
                body = ET.SubElement(openrosa_root, "body")

                # Requirement 5: Cached/Predictable Identifiers mapped identically
                items = payload.get("protocol", {}).get("items", [])
                for item in items:
                    item_id = item.get("id")
                    if not item_id:
                        item_id = f"item_{uuid.uuid4().hex[:8]}"

                    item_name = item.get("name", "Unknown Field")
                    item_type = item.get("type", "string")

                    # Add ODM Definition
                    ET.SubElement(
                        meta_odm,
                        "ItemDef",
                        OID=item_id,
                        Name=item_name,
                        DataType=item_type,
                    )

                    # Add OpenRosa XForm Elements
                    ET.SubElement(data, item_id)
                    ET.SubElement(
                        model, "xf:bind", nodeset=f"/{item_id}", type=f"{item_type}"
                    )

                    ui_input = ET.SubElement(body, "xf:input", ref=f"/{item_id}")
                    label = ET.SubElement(ui_input, "xf:label")
                    label.text = item_name

                # Format outputs
                odm_str = minidom.parseString(ET.tostring(odm_root)).toprettyxml(
                    indent="  "
                )
                openrosa_str = minidom.parseString(
                    ET.tostring(openrosa_root)
                ).toprettyxml(indent="  ")

                job.odm_payload = odm_str
                job.openrosa_payload = openrosa_str
                job.status = "COMPLETED"

            except Exception as e:
                # Constraint: fail gracefully
                job.status = "FAILED"
                job.error_message = str(e)
            finally:
                # Update job execution status
                await session.flush()
                current_session.reset(token)
