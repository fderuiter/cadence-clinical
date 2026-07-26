import os

from jinja2 import Environment, FileSystemLoader, select_autoescape
from sae_icsr import IndividualCaseSafetyReport


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
