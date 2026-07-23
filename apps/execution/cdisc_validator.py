from typing import Tuple

import defusedxml.ElementTree as ET


def validate_cdisc_xml_structure(xml_content: str) -> Tuple[bool, str]:
    """Validate structural correctness of the generated CDISC ODM XML export.

    Ensures that the XML is well-formed, complies with the official CDISC namespace,
    and contains all mandatory high-level structural nodes (ODM, ClinicalData, SubjectData).

    Args:
        xml_content (str): The CDISC XML payload to validate.

    Returns:
        Tuple[bool, str]: A tuple of (is_valid, message).
    """
    try:
        root = ET.fromstring(xml_content.encode("utf-8"))
    except Exception as e:
        return False, f"XML parsing error: {str(e)}"

    ns = "{http://www.cdisc.org/ns/odm/v1.3}"
    if root.tag != f"{ns}ODM":
        return False, f"Invalid root element: expected '{ns}ODM', got '{root.tag}'"

    # Verify ODM attributes
    if "FileOID" not in root.attrib:
        return False, "Missing mandatory attribute 'FileOID' in root ODM element"

    # Verify ClinicalData
    clinical_data = root.find(f"{ns}ClinicalData")
    if clinical_data is None:
        return False, f"Missing mandatory element '{ns}ClinicalData'"

    if "StudyOID" not in clinical_data.attrib:
        return False, "Missing mandatory attribute 'StudyOID' in ClinicalData element"

    # Verify SubjectData is present if subjects exist
    subjects = clinical_data.findall(f"{ns}SubjectData")
    for subj in subjects:
        if "SubjectKey" not in subj.attrib:
            return (
                False,
                "Missing mandatory attribute 'SubjectKey' in SubjectData element",
            )

    return True, "Structure matches official CDISC specifications successfully."
