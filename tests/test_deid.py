from apps.interop.fhir_adapter import deidentify_free_text, strip_pii_from_patient
from packages.deid.detector import DeidDetector, redact_text, resolve_overlaps
from packages.deid.models import ComplianceProfile, DetectionResult, DetectorCategory


def test_basic_detection_results():
    """
    Test that DetectionResult pydantic model initializes properly.
    """
    res = DetectionResult(category="email", start=5, end=20, value="test@example.com")
    assert res.category == "email"
    assert res.start == 5
    assert res.end == 20
    assert res.value == "test@example.com"


def test_email_detector():
    detector = DeidDetector()
    text = (
        "Please email the patient at john.doe@hacker.net or jane-smith_12@clinic.org."
    )
    results = detector.detect(text, profile=ComplianceProfile.HIPAA)

    emails = [r for r in results if r.category == DetectorCategory.EMAIL]
    assert len(emails) == 2

    assert emails[0].value == "john.doe@hacker.net"
    assert emails[0].start == 28
    assert emails[0].end == 47

    assert emails[1].value == "jane-smith_12@clinic.org"


def test_phone_fax_detector():
    detector = DeidDetector()
    text = (
        "Call us at +1-555-019-9999 or fax at (555) 019-1234. "
        "Local phone is 555-4321. Date 2026-07-22 should not be matched."
    )
    results = detector.detect(text, profile=ComplianceProfile.HIPAA)

    phones = [r for r in results if r.category == DetectorCategory.TELEPHONE_FAX]
    assert len(phones) == 3

    assert "+1-555-019-9999" in [p.value for p in phones]
    assert "(555) 019-1234" in [p.value for p in phones]
    assert "555-4321" in [p.value for p in phones]

    # Ensure date 2026-07-22 is not matched as telephone_fax
    assert "2026-07-22" not in [p.value for p in phones]


def test_ssn_national_id_detector():
    detector = DeidDetector()
    text = "SSN is 123-45 - wait, it is 123-45-6789. UK NINO is PP123456C."
    results = detector.detect(text, profile=ComplianceProfile.HIPAA)

    ssns = [r for r in results if r.category == DetectorCategory.SSN_NATIONAL_ID]
    assert len(ssns) == 2
    assert "123-45-6789" in [s.value for s in ssns]
    assert "PP123456C" in [s.value for s in ssns]


def test_dates_detector():
    detector = DeidDetector()
    text = (
        "Admitted on 2026-07-22 and discharged on 08/15/2026. "
        "Born on 12-Jan-1980 or January 15, 1990."
    )
    results = detector.detect(text, profile=ComplianceProfile.HIPAA)

    dates = [r for r in results if r.category == DetectorCategory.DATES]
    assert len(dates) >= 4

    values = [d.value for d in dates]
    assert "2026-07-22" in values
    assert "08/15/2026" in values
    assert "12-Jan-1980" in values
    assert "January 15, 1990" in values


def test_zip_geographic_detector():
    detector = DeidDetector()
    text = "Located in Boston, MA 02111 or London SW1A 1AA or Toronto M5V 2L7."
    results = detector.detect(text, profile=ComplianceProfile.HIPAA)

    geos = [r for r in results if r.category == DetectorCategory.ZIP_GEOGRAPHIC]
    assert len(geos) == 3
    assert "02111" in [g.value for g in geos]
    assert "SW1A 1AA" in [g.value for g in geos]
    assert "M5V 2L7" in [g.value for g in geos]


def test_urls_detector():
    detector = DeidDetector()
    text = "Visit https://clinicaltrials.gov/ct2/show/NCT12345 or www.google.com for details."
    results = detector.detect(text, profile=ComplianceProfile.HIPAA)

    urls = [r for r in results if r.category == DetectorCategory.URLS]
    assert len(urls) == 2
    assert "https://clinicaltrials.gov/ct2/show/NCT12345" in [u.value for u in urls]
    assert "www.google.com" in [u.value for u in urls]


def test_ip_mac_detector():
    detector = DeidDetector()
    text = "Server IP: 192.168.1.105, IPv6: 2001:db8::ff00:42:8329, MAC: 00:0a:95:9d:68:16."
    results = detector.detect(text, profile=ComplianceProfile.HIPAA)

    ips = [r for r in results if r.category == DetectorCategory.IP_MAC_ADDRESSES]
    assert len(ips) == 3
    assert "192.168.1.105" in [i.value for i in ips]
    assert "2001:db8::ff00:42:8329" in [i.value for i in ips]
    assert "00:0a:95:9d:68:16" in [i.value for i in ips]


def test_medical_record_account_detector():
    detector = DeidDetector()
    text = "Patient MRN is MRN-998822, EHR record EHR-1002, NHS number is 456-789-0123."
    results = detector.detect(text, profile=ComplianceProfile.HIPAA)

    meds = [r for r in results if r.category == DetectorCategory.MEDICAL_RECORD_ACCOUNT]
    assert len(meds) == 3
    assert "MRN-998822" in [m.value for m in meds]
    assert "EHR-1002" in [m.value for m in meds]
    assert "456-789-0123" in [m.value for m in meds]


def test_custom_literal_terms():
    detector = DeidDetector()
    text = "Subject initials are JD. Name is John Doe. Symbol term is $pecial*."
    custom_terms = ["John Doe", "JD", "$pecial*"]

    # Scan with custom terms
    results = detector.detect(
        text, profile=ComplianceProfile.HIPAA, custom_terms=custom_terms
    )

    customs = [r for r in results if r.category == DetectorCategory.CUSTOM]
    assert len(customs) == 3
    assert "John Doe" in [c.value for c in customs]
    assert "JD" in [c.value for c in customs]
    assert "$pecial*" in [c.value for c in customs]


def test_compliance_profiles():
    detector = DeidDetector()
    text = "Email is admin@trial.org, IP is 10.0.0.1, date is 2026-07-22."

    # 1. HIPAA: enables email, ip, dates
    hipaa_res = detector.detect(text, profile=ComplianceProfile.HIPAA)
    categories_hipaa = {r.category for r in hipaa_res}
    assert DetectorCategory.EMAIL in categories_hipaa
    assert DetectorCategory.IP_MAC_ADDRESSES in categories_hipaa
    assert DetectorCategory.DATES in categories_hipaa

    # 2. EU_CTR: only email and dates enabled (IP omitted)
    ctr_res = detector.detect(text, profile=ComplianceProfile.EU_CTR)
    categories_ctr = {r.category for r in ctr_res}
    assert DetectorCategory.EMAIL in categories_ctr
    assert DetectorCategory.DATES in categories_ctr
    assert DetectorCategory.IP_MAC_ADDRESSES not in categories_ctr


def test_overlap_resolution_deterministic():
    # Setup overlapping matches
    results = [
        # Match 1: "john.doe" (custom term)
        DetectionResult(category="custom", start=10, end=18, value="john.doe"),
        # Match 2: "john.doe@example.com" (email) - wider, covers Match 1
        DetectionResult(
            category="email", start=10, end=30, value="john.doe@example.com"
        ),
        # Match 3: "example.com" (url) - nested inside Match 2
        DetectionResult(category="urls", start=19, end=30, value="example.com"),
        # Match 4: "123-456" (custom) - partially overlaps with Match 5
        DetectionResult(category="custom", start=35, end=42, value="123-456"),
        # Match 5: "456-7890" (phone) - partially overlaps with Match 4
        DetectionResult(category="telephone_fax", start=39, end=47, value="456-7890"),
        # Match 6: tie-breaker on identical range
        DetectionResult(category="custom", start=50, end=55, value="match"),
        DetectionResult(category="zip_geographic", start=50, end=55, value="match"),
    ]

    resolved = resolve_overlaps(results)

    # Verify non-overlapping results
    # 1. john.doe@example.com should override john.doe and example.com (wider interval)
    # 2. Between 123-456 (35-42) and 456-7890 (39-47), the first/wider interval processed
    #    First starting: 123-456 (start 35) gets accepted. 456-7890 (start 39) overlaps and is dropped.
    # 3. For the tie, category 'custom' is alphabetical before 'zip_geographic', or sorted deterministically.

    values = [r.value for r in resolved]
    assert "john.doe@example.com" in values
    assert "john.doe" not in values
    assert "example.com" not in values

    assert "123-456" in values
    assert "456-7890" not in values

    # Check that range 50-55 is represented exactly once
    ties = [r for r in resolved if r.start == 50 and r.end == 55]
    assert len(ties) == 1


def test_redact_text_sequential():
    text = "Patient Jane Doe (initials JD, birthdate 1980-01-01) is admitted."
    detector = DeidDetector()
    results = detector.detect(
        text, profile=ComplianceProfile.HIPAA, custom_terms=["Jane Doe", "JD"]
    )

    # Default category replacement: [CUSTOM], [DATES]
    redacted_default = redact_text(text, results)
    assert "[CUSTOM]" in redacted_default
    assert "[DATES]" in redacted_default
    assert "Jane Doe" not in redacted_default
    assert "1980-01-01" not in redacted_default

    # Custom placeholder function
    redacted_custom = redact_text(text, results, placeholder_func=lambda x: "REDACTED")
    assert (
        redacted_custom
        == "Patient REDACTED (initials REDACTED, birthdate REDACTED) is admitted."
    )


def test_fhir_narrative_and_notes_integration():
    """
    Test FHIR narrative and note de-identification functions.
    """
    mock_patient = {
        "resourceType": "Patient",
        "id": "PAT-99",
        "name": [{"family": "Smith", "given": ["Jane"]}],
        "text": {
            "status": "generated",
            "div": "<div>Patient Jane Smith with email j.smith@clinic.org.</div>",
        },
        "note": [
            {
                "authorString": "Dr. House",
                "text": "Call SSN 123-45-6789 or phone 555-0199.",
            }
        ],
    }

    stripped = strip_pii_from_patient(mock_patient)

    # Direct identifiers stripped
    assert "name" not in stripped

    # Narrative redacted
    assert "Jane Smith" not in stripped["text"]["div"]
    assert "[EMAIL]" in stripped["text"]["div"]

    # Note redacted
    assert "123-45-6789" not in stripped["note"][0]["text"]
    assert "[SSN_NATIONAL_ID]" in stripped["note"][0]["text"]
    assert "[TELEPHONE_FAX]" in stripped["note"][0]["text"]


def test_deidentify_free_text_direct():
    text = "John's SSN is 000-12-3456 and email is john@me.com."
    redacted = deidentify_free_text(
        text, ComplianceProfile.HIPAA, custom_terms=["John"]
    )
    assert "[CUSTOM]" in redacted
    assert "[SSN_NATIONAL_ID]" in redacted
    assert "[EMAIL]" in redacted
    assert "John" not in redacted
    assert "000-12-3456" not in redacted
    assert "john@me.com" not in redacted


def test_cli_get_line_and_col():
    from packages.deid.cli import get_line_and_col

    content = "line1\nline2\nline3"
    # 'l' in line2 is at index 6
    line, col = get_line_and_col(content, 6)
    assert line == 2
    assert col == 1


def test_cli_should_scan_file(tmp_path):
    from packages.deid.cli import should_scan_file

    py_file = tmp_path / "test.py"
    py_file.write_text("print('hello')", encoding="utf-8")
    assert should_scan_file(str(py_file)) is True

    bin_file = tmp_path / "test.bin"
    bin_file.write_bytes(b"\x00\x01\x02\x03")
    assert should_scan_file(str(bin_file)) is False


def test_cli_load_gitignore_patterns(tmp_path):
    import os

    from packages.deid.cli import is_locally_ignored, load_gitignore_patterns

    root_dir = str(tmp_path)
    gitignore = tmp_path / ".gitignore"
    gitignore.write_text(
        "# This is comment\n*.log\n/build/\n!keep.log", encoding="utf-8"
    )

    patterns = load_gitignore_patterns(root_dir)
    assert len(patterns) == 3
    assert patterns[0] == (False, "*.log")
    assert patterns[1] == (False, "/build/")
    assert patterns[2] == (True, "keep.log")

    assert (
        is_locally_ignored(os.path.join(root_dir, "app.log"), patterns, root_dir)
        is True
    )
    assert (
        is_locally_ignored(os.path.join(root_dir, "keep.log"), patterns, root_dir)
        is False
    )
    assert (
        is_locally_ignored(
            os.path.join(root_dir, "build", "something"), patterns, root_dir
        )
        is True
    )


def test_cli_main_clean(tmp_path, monkeypatch):
    import os
    import sys

    from packages.deid.cli import main

    test_file = tmp_path / "clean.txt"
    test_file.write_text("This is completely clean text.", encoding="utf-8")

    monkeypatch.setattr(sys, "argv", ["deid-scan", str(test_file)])

    exit_code = None

    def mock_exit(code):
        nonlocal exit_code
        exit_code = code

    monkeypatch.setattr(sys, "exit", mock_exit)

    monkeypatch.setattr(os, "getcwd", lambda: str(tmp_path))

    main()
    assert exit_code == 0


def test_cli_main_violation(tmp_path, monkeypatch):
    import os
    import sys

    from packages.deid.cli import main

    test_file = tmp_path / "dirty.txt"
    test_file.write_text("My email is patient@hospital.org", encoding="utf-8")

    monkeypatch.setattr(sys, "argv", ["deid-scan", str(test_file)])

    exit_code = None

    def mock_exit(code):
        nonlocal exit_code
        exit_code = code

    monkeypatch.setattr(sys, "exit", mock_exit)

    monkeypatch.setattr(os, "getcwd", lambda: str(tmp_path))

    main()
    assert exit_code == 1
