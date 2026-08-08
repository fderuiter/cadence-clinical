import os

from jinja2 import Environment, FileSystemLoader, select_autoescape

from apps.safety.src.domain.sae_icsr import IndividualCaseSafetyReport
from apps.safety.validator import validate_e2b_xml_structure


def render_icsr_to_xml(icsr: IndividualCaseSafetyReport) -> str:
    """Render an IndividualCaseSafetyReport model into an E2B(R3) ICSR XML string.

    Args:
        icsr (IndividualCaseSafetyReport): The ICSR model instance.

    Returns:
        str: The rendered XML string.
    """
    templates_dir = os.path.join(os.path.dirname(__file__), "templates")
    env = Environment(
        loader=FileSystemLoader(templates_dir),
        autoescape=select_autoescape(["html", "xml"]),
    )
    template = env.get_template("e2b_r3_icsr.xml.j2")
    return template.render(icsr=icsr)


def generate_e2b_xml(icsr: IndividualCaseSafetyReport) -> str:
    """Render the ICSR template and run the structural validator before returning,
    mirroring generate_cdisc_export_xml.

    Args:
        icsr (IndividualCaseSafetyReport): The ICSR model instance.

    Returns:
        str: The validated rendered XML string.

    Raises:
        ValueError: If the generated XML fails structural validation.
    """
    xml_content = render_icsr_to_xml(icsr)
    is_valid, msg = validate_e2b_xml_structure(xml_content)
    if not is_valid:
        raise ValueError(f"Generated E2B XML failed structural schema checks: {msg}")
    return xml_content
