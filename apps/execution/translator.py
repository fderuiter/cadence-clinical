import os
import re
import uuid
from typing import Any

import defusedxml.minidom as minidom
from jinja2 import Environment, FileSystemLoader, select_autoescape

from apps.execution.database.context import audit_context, current_session
from apps.execution.database.models import TranslationJob

# Setup Jinja2 environment
TEMPLATE_DIR = os.path.join(os.path.dirname(__file__), "templates")
env = Environment(
    loader=FileSystemLoader(TEMPLATE_DIR),
    autoescape=select_autoescape(default_for_string=True, default=True),
)


def sanitize_identifier(raw_id: Any) -> str:
    """Sanitize identifier values to be valid XML tag names deterministically.

    Replacing spaces and non-alphanumeric characters, and adjusting leading digits.
    Existing valid identifier formats (alphanumeric strings starting with letters)
    remain unchanged during translation.
    Falls back to standard unique ID generation if the original identifier is entirely missing.
    """
    if not raw_id or not isinstance(raw_id, str) or not raw_id.strip():
        return f"item_{uuid.uuid4().hex[:8]}"

    # Strip leading and trailing whitespaces
    stripped_id = raw_id.strip()

    # If it is already a valid identifier (starts with letter, followed by alphanumeric/underscore), return it unchanged
    if re.match(r"^[a-zA-Z][a-zA-Z0-9_]*$", stripped_id):
        return stripped_id

    # Otherwise, perform sanitization
    # 1. Adjust leading digits: if it starts with a digit, prepend "item_"
    starts_with_digit = re.match(r"^\d", stripped_id) is not None

    # 2. Map characters: replace spaces and non-alphanumeric characters
    sanitized_chars = []
    for c in stripped_id:
        if c.isalnum() or c == "_":
            sanitized_chars.append(c)
        elif c == " ":
            sanitized_chars.append("_")
        else:
            # Deterministic mapping for special characters to avoid collisions with other sanitized values
            sanitized_chars.append(f"_{ord(c):02x}")

    sanitized_str = "".join(sanitized_chars)

    if starts_with_digit:
        sanitized_str = f"item_{sanitized_str}"

    # Ensure it starts with a valid character
    if not re.match(r"^[a-zA-Z_]", sanitized_str):
        sanitized_str = f"item_{sanitized_str}"

    return sanitized_str


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


def compile_condition_to_xpath(node: Any) -> str:
    """Recursively compiles a structured rule condition (dict or ExpressionNode) into an XPath expression string.

    References to fields are sanitized using `sanitize_identifier`.
    """
    if not node:
        return ""

    # Standardize dictionary vs ExpressionNode
    node_type = getattr(node, "type", None) or node.get("type")
    if not node_type:
        return ""

    if node_type == "constant":
        val = (
            getattr(node, "value", None)
            if hasattr(node, "value")
            else node.get("value")
        )
        if val is True:
            return "true()"
        if val is False:
            return "false()"
        if val is None:
            return ""
        if isinstance(val, str):
            return f"'{val}'"
        return str(val)

    elif node_type == "field_ref":
        ref = (
            getattr(node, "field_ref", None)
            if hasattr(node, "field_ref")
            else node.get("field_ref")
        )
        if not ref:
            return ""
        field_id = (
            getattr(ref, "field_id", None)
            if hasattr(ref, "field_id")
            else ref.get("field_id")
        )
        if not field_id:
            return ""
        sanitized = sanitize_identifier(field_id)
        return f"/{sanitized}"

    elif node_type == "logical":
        operator = (
            getattr(node, "operator", None)
            if hasattr(node, "operator")
            else node.get("operator")
        )
        operands = (
            getattr(node, "operands", None)
            if hasattr(node, "operands")
            else node.get("operands")
        )
        if not operator or not operands:
            return ""
        if operator == "not":
            return f"not({compile_condition_to_xpath(operands[0])})"
        op_lower = f" {operator.lower()} "
        compiled_ops = [compile_condition_to_xpath(op) for op in operands]
        return f"({op_lower.join(compiled_ops)})"

    elif node_type == "comparison":
        operator = (
            getattr(node, "operator", None)
            if hasattr(node, "operator")
            else node.get("operator")
        )
        operands = (
            getattr(node, "operands", None)
            if hasattr(node, "operands")
            else node.get("operands")
        )
        if not operator or not operands or len(operands) < 2:
            return ""
        op_symbol = "=" if operator == "==" else operator
        left = compile_condition_to_xpath(operands[0])
        right = compile_condition_to_xpath(operands[1])
        return f"({left} {op_symbol} {right})"

    elif node_type == "function":
        operator = (
            getattr(node, "operator", None)
            if hasattr(node, "operator")
            else node.get("operator")
        )
        operands = (
            getattr(node, "operands", None)
            if hasattr(node, "operands")
            else node.get("operands")
        )
        if not operator or not operands:
            return ""
        if operator == "is_empty":
            return f"empty({compile_condition_to_xpath(operands[0])})"
        elif operator == "is_not_empty":
            return f"not(empty({compile_condition_to_xpath(operands[0])}))"
        compiled_ops = [compile_condition_to_xpath(op) for op in operands]
        return f"{operator}({', '.join(compiled_ops)})"

    return ""


async def process_translation(
    study_id: str,
    payload: dict[str, Any],
    session_factory: Any,
    user_id: str | None = None,
    change_reason: str | None = None,
    job_id: str | None = None,
) -> None:
    """Background worker that translates USDM payload into CDISC ODM and OpenRosa XML layouts.

    Args:
        study_id (str): The unique identifier of the source study.
        payload (dict[str, Any]): The raw USDM protocol payload.
        session_factory (Any): The SQLAlchemy asynchronous session factory.
        user_id (str | None): The user ID to attribute database modifications to.
        change_reason (str | None): The reason/justification for database modifications.
        job_id (str | None): The pre-generated UUID for the translation job.

    Returns:
        None
    """
    token = None
    with audit_context(user_id, change_reason):
        try:
            async with session_factory() as session:
                token = current_session.set(session)
                actual_job_id = job_id if job_id else str(uuid.uuid4())

                try:
                    async with session.begin():
                        job = TranslationJob(
                            id=actual_job_id, study_id=study_id, status="PROCESSING"
                        )
                        session.add(job)
                        await session.flush()

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
                            item_id = sanitize_identifier(item.get("id"))

                            item_name = item.get("name", "Unknown Field")
                            item_type = item.get("type", "string")
                            appearance = extract_appearance(item)

                            # Parse and compile rules
                            relevants = []
                            constraints = []
                            constraint_messages = []
                            item_rules = item.get("rules", [])
                            for r in item_rules:
                                r_type = r.get("type")
                                condition = r.get("condition")
                                if not condition:
                                    continue

                                compiled_xpath = compile_condition_to_xpath(condition)
                                if not compiled_xpath:
                                    continue

                                if r_type == "skip_logic":
                                    action = r.get("action", "show")
                                    if action == "hide":
                                        expr = f"not({compiled_xpath})"
                                    else:
                                        expr = compiled_xpath
                                    relevants.append(expr)

                                elif r_type == "constraint":
                                    constraints.append(compiled_xpath)
                                    msg = r.get("query_message")
                                    if msg:
                                        constraint_messages.append(msg)

                            relevant_expr = (
                                " and ".join(
                                    f"({r})" if len(relevants) > 1 else r
                                    for r in relevants
                                )
                                if relevants
                                else None
                            )
                            constraint_expr = (
                                " and ".join(
                                    f"({c})" if len(constraints) > 1 else c
                                    for c in constraints
                                )
                                if constraints
                                else None
                            )
                            constraint_msg = (
                                "; ".join(constraint_messages)
                                if constraint_messages
                                else None
                            )

                            processed_items.append(
                                {
                                    "id": item_id,
                                    "name": item_name,
                                    "type": item_type,
                                    "appearance": appearance,
                                    "relevant": relevant_expr,
                                    "constraint": constraint_expr,
                                    "constraint_message": constraint_msg,
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
                            """
                            Format an XML string with indentation for better readability.

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
                    # Transaction has been rolled back. Now save the failed status in a new transaction.
                    async with session.begin():
                        failed_job = TranslationJob(
                            id=actual_job_id,
                            study_id=study_id,
                            status="FAILED",
                            error_message=str(e),
                        )
                        session.add(failed_job)
        finally:
            if token is not None:
                current_session.reset(token)
