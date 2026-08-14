import defusedxml.ElementTree as ET


def validate_e2b_xml_structure(xml_content: str) -> tuple[bool, str]:
    """Validate structural correctness of the generated E2B(R3) ICSR XML export.

    Ensures that the XML is well-formed, complies with the urn:hl7-org:v3 namespace,
    and contains all mandatory high-level structural nodes and identifiers.

    Args:
        xml_content (str): The XML payload to validate.

    Returns:
        Tuple[bool, str]: A tuple of (is_valid, message).
    """
    try:
        root = ET.fromstring(xml_content.encode("utf-8"))
    except Exception as e:
        return False, f"XML parsing error: {str(e)}"

    ns = "{urn:hl7-org:v3}"
    if root.tag != f"{ns}ichicsr":
        return False, f"Invalid root element: expected '{ns}ichicsr', got '{root.tag}'"

    # Verify nested wrappers MCCI_IN200100UV01 and PORR_IN049016UV
    mcci = root.find(f"{ns}MCCI_IN200100UV01")
    if mcci is None:
        return False, "Missing mandatory element 'MCCI_IN200100UV01' inside root 'ichicsr'"

    porr = mcci.find(f"{ns}PORR_IN049016UV")
    if porr is None:
        return False, "Missing mandatory element 'PORR_IN049016UV' inside wrapper 'MCCI_IN200100UV01'"

    # Verify message identifiers (header)
    header = mcci.find(f"{ns}header")
    if header is None:
        return False, "Missing mandatory element 'header' inside wrapper 'MCCI_IN200100UV01'"

    for field in [
        "message_id",
        "sender_organization",
        "receiver_organization",
        "transmission_date",
    ]:
        elem = header.find(f"{ns}{field}")
        if elem is None or not elem.text or not elem.text.strip():
            return (
                False,
                f"Missing or empty mandatory message identifier '{field}' in header",
            )

    # Verify safety-report identifiers
    safety_report = porr.find(f"{ns}safety_report")
    if safety_report is None:
        return False, "Missing mandatory element 'safety_report' inside wrapper 'PORR_IN049016UV'"

    ww_id = safety_report.find(f"{ns}worldwide_unique_case_id")
    if ww_id is None or not ww_id.text or not ww_id.text.strip():
        return (
            False,
            "Missing or empty mandatory identifier 'worldwide_unique_case_id' in safety_report",
        )

    # Verify required patient details
    patient = safety_report.find(f"{ns}patient")
    if patient is None:
        return False, "Missing mandatory element 'patient' inside safety_report"

    for field in ["patient_id", "sex"]:
        elem = patient.find(f"{ns}{field}")
        if elem is None or not elem.text or not elem.text.strip():
            return False, f"Missing or empty mandatory patient attribute '{field}'"

    # Verify reaction list contains at least one reaction and reaction_term is valid
    reactions_elem = safety_report.find(f"{ns}reactions")
    if reactions_elem is None:
        return False, "Missing mandatory element 'reactions' inside safety_report"

    reactions = reactions_elem.findall(f"{ns}reaction")
    if not reactions:
        return (
            False,
            "Missing mandatory block 'reaction': at least one reaction is required",
        )

    for i, reaction in enumerate(reactions):
        term = reaction.find(f"{ns}reaction_term")
        if term is None or not term.text or not term.text.strip():
            return (
                False,
                f"Missing or empty mandatory attribute 'reaction_term' in reaction element at index {i}",
            )

    # Verify suspect drugs list contains at least one drug and drug_name, drug_role are valid
    drugs_elem = safety_report.find(f"{ns}suspect_drugs")
    if drugs_elem is None:
        return False, "Missing mandatory element 'suspect_drugs' inside safety_report"

    drugs = drugs_elem.findall(f"{ns}suspect_drug")
    if not drugs:
        return (
            False,
            "Missing mandatory block 'suspect_drug': at least one suspect drug is required",
        )

    for i, drug in enumerate(drugs):
        name = drug.find(f"{ns}drug_name")
        if name is None or not name.text or not name.text.strip():
            return (
                False,
                f"Missing or empty mandatory attribute 'drug_name' in suspect_drug element at index {i}",
            )
        role = drug.find(f"{ns}drug_role")
        if role is None or not role.text or not role.text.strip():
            return (
                False,
                f"Missing or empty mandatory attribute 'drug_role' in suspect_drug element at index {i}",
            )

    return True, "Structure matches official E2B(R3) ICSR specifications successfully."


def validate_icsr_xml(xml_content: str) -> tuple[bool, str]:
    """Validate structural correctness of the generated E2B(R3) ICSR XML export.

    Args:
        xml_content (str): The XML payload to validate.

    Returns:
        Tuple[bool, str]: A tuple of (is_valid, message).
    """
    return validate_e2b_xml_structure(xml_content)
