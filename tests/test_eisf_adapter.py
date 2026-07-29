import pytest

from apps.eisf.adapter import (
    DocumentClassification,
    classify_incoming_document,
    derive_correlation_key,
    map_eisf_to_etmf,
    map_etmf_to_eisf,
)


class MockDocument:
    """
    Simple mock document object to test classification with objects.
    """

    def __init__(self, version_index: int, content_checksum: str):
        self.version_index = version_index
        self.content_checksum = content_checksum


def test_deterministic_bidirectional_mapping_success():
    """
    Verify that mapping is deterministic and covered for all supported binder artifacts,
    and that the same logical artifact maps identically in both directions.
    """
    supported_artifacts = [
        (
            "Investigator & Staff",
            "Investigator CV",
            5,
            "05.02",
            "Investigator CV",
            "05.02.03",
        ),
        (
            "Investigator & Staff",
            "Delegation of Authority Log",
            5,
            "05.02",
            "Delegation of Authority Log",
            "05.02.04",
        ),
        (
            "Protocols & Amendments",
            "Approved Protocol",
            1,
            "01.01",
            "Clinical Trial Protocol",
            "01.01.01",
        ),
        (
            "Protocols & Amendments",
            "Protocol Sign-off",
            1,
            "01.01",
            "Protocol Sign-off",
            "01.01.03",
        ),
        (
            "Regulatory Approvals",
            "IRB Approval",
            4,
            "04.01",
            "IRB/IEC Approval",
            "04.01.01",
        ),
        (
            "Regulatory Approvals",
            "FDA Form 1572",
            5,
            "05.02",
            "FDA Form 1572",
            "05.02.01",
        ),
    ]

    for (
        binder_class,
        eisf_type,
        zone,
        section,
        etmf_type,
        etmf_code,
    ) in supported_artifacts:
        # 1. Forward Map (eISF -> eTMF)
        etmf_result = map_eisf_to_etmf(binder_class, eisf_type)
        assert etmf_result["zone"] == zone
        assert etmf_result["section"] == section
        assert etmf_result["artifact_type"] == etmf_type
        assert etmf_result["artifact_code"] == etmf_code

        # 2. Reverse Map (eTMF -> eISF)
        eisf_result = map_etmf_to_eisf(zone, section, etmf_type, etmf_code)
        assert eisf_result["binder_classification"] == binder_class
        assert eisf_result["artifact_type"] == eisf_type

        # 3. Test round-trip identity
        round_trip_etmf = map_eisf_to_etmf(
            eisf_result["binder_classification"], eisf_result["artifact_type"]
        )
        assert round_trip_etmf == etmf_result


def test_mapping_normalization():
    """
    Verify that lookups are case-insensitive and handle whitespace stripping robustly.
    """
    # Forward mapping with messy case and spacing
    etmf_res = map_eisf_to_etmf("  iNvEsTiGaToR & sTaFf  ", "  iNvEsTiGaToR cV  ")
    assert etmf_res["zone"] == 5
    assert etmf_res["section"] == "05.02"
    assert etmf_res["artifact_type"] == "Investigator CV"
    assert etmf_res["artifact_code"] == "05.02.03"

    # Reverse mapping with messy case and spacing
    eisf_res = map_etmf_to_eisf(
        1, "  01.01  ", "  cLInIcAl TrIaL pRoToCoL  ", "  01.01.01  "
    )
    assert eisf_res["binder_classification"] == "Protocols & Amendments"
    assert eisf_res["artifact_type"] == "Approved Protocol"

    # Reverse mapping without specifying code
    eisf_res_no_code = map_etmf_to_eisf(1, "01.01", "Clinical Trial Protocol")
    assert eisf_res_no_code["binder_classification"] == "Protocols & Amendments"
    assert eisf_res_no_code["artifact_type"] == "Approved Protocol"


def test_mapping_failures():
    """
    Verify that unsupported mapping inputs correctly raise ValueError.
    """
    with pytest.raises(ValueError, match="Unsupported eISF mapping"):
        map_eisf_to_etmf("Unknown Section", "Some Artifact")

    with pytest.raises(ValueError, match="Unsupported eTMF reverse mapping"):
        map_etmf_to_eisf(99, "99.99", "Unknown Artifact Type")


def test_derive_correlation_key():
    """
    Verify that derive_correlation_key produces stable, deterministic, normalized keys.
    """
    key_1 = derive_correlation_key(
        "Study-A", "Site-1", "Investigator & Staff", "Investigator CV"
    )
    key_2 = derive_correlation_key(
        "  study-a  ", "  site-1  ", "  investigator & staff  ", "  investigator cv  "
    )

    assert key_1 == key_2
    assert key_1 == "corr:study-a:site-1:investigator & staff:investigator cv"


def test_classify_incoming_document_new():
    """
    Verify that classification is NEW if there are no existing documents for the correlation key.
    """
    classification, matched_doc = classify_incoming_document(
        incoming_checksum="checksum-abc", existing_documents_for_key=[]
    )
    assert classification == DocumentClassification.NEW
    assert matched_doc is None


def test_classify_incoming_document_duplicate_dict():
    """
    Verify that identical content (matching checksum) is classified as DUPLICATE (no-op)
    using list of dictionaries representing existing documents.
    """
    existing_docs = [
        {"version_index": 1, "content_checksum": "checksum-111"},
        {"version_index": 2, "content_checksum": "checksum-222"},
    ]

    # Matching the first one
    classification, matched_doc = classify_incoming_document(
        incoming_checksum="checksum-111", existing_documents_for_key=existing_docs
    )
    assert classification == DocumentClassification.DUPLICATE
    assert matched_doc == existing_docs[0]

    # Matching the second one
    classification, matched_doc = classify_incoming_document(
        incoming_checksum="checksum-222", existing_documents_for_key=existing_docs
    )
    assert classification == DocumentClassification.DUPLICATE
    assert matched_doc == existing_docs[1]


def test_classify_incoming_document_duplicate_object():
    """
    Verify that identical content (matching checksum) is classified as DUPLICATE (no-op)
    using list of objects representing existing documents.
    """
    existing_docs = [
        MockDocument(version_index=1, content_checksum="checksum-111"),
        MockDocument(version_index=2, content_checksum="checksum-222"),
    ]

    classification, matched_doc = classify_incoming_document(
        incoming_checksum="checksum-222", existing_documents_for_key=existing_docs
    )
    assert classification == DocumentClassification.DUPLICATE
    assert matched_doc == existing_docs[1]


def test_classify_incoming_document_changed_dict():
    """
    Verify that changed content (different content checksum) for the same correlation key
    is classified as CHANGED (new version/update) rather than a duplicate, returning the
    latest version of the document (dictionary format).
    """
    existing_docs = [
        {"version_index": 1, "content_checksum": "checksum-111"},
        {"version_index": 3, "content_checksum": "checksum-333"},
        {"version_index": 2, "content_checksum": "checksum-222"},
    ]

    classification, latest_doc = classify_incoming_document(
        incoming_checksum="checksum-new-updated",
        existing_documents_for_key=existing_docs,
    )
    assert classification == DocumentClassification.CHANGED
    # Should resolve to the max version (version_index 3)
    assert latest_doc == existing_docs[1]


def test_classify_incoming_document_changed_object():
    """
    Verify that changed content (different content checksum) for the same correlation key
    is classified as CHANGED (new version/update) rather than a duplicate, returning the
    latest version of the document (object format).
    """
    existing_docs = [
        MockDocument(version_index=1, content_checksum="checksum-111"),
        MockDocument(version_index=4, content_checksum="checksum-444"),
        MockDocument(version_index=2, content_checksum="checksum-222"),
    ]

    classification, latest_doc = classify_incoming_document(
        incoming_checksum="checksum-new-updated",
        existing_documents_for_key=existing_docs,
    )
    assert classification == DocumentClassification.CHANGED
    # Should resolve to the max version (version_index 4)
    assert latest_doc == existing_docs[1]


def test_eisf_mappings_resolve_through_active_catalog():
    """
    Verify that all forward mappings defined in the eISF adapter successfully
    resolve to standard, valid artifacts, sections, and zones in the active complete catalog.
    """
    from tmf_reference_model import get_active_catalog, resolve_artifact

    from apps.eisf.adapter import FORWARD_MAPPING

    active_catalog = get_active_catalog()
    assert active_catalog.version == "v3.2.0-complete"

    for (binder_sec, art_type), (
        zone,
        section,
        etmf_art_type,
        etmf_code,
    ) in FORWARD_MAPPING.items():
        # Resolve artifact dynamically through the taxonomy catalog
        resolved = resolve_artifact(active_catalog.version, code=etmf_code)

        # Verify correctness of the resolution
        artifact = resolved["artifact"]
        parent_section = resolved["section"]
        parent_zone = resolved["zone"]

        assert artifact.code == etmf_code
        assert artifact.section_code == section
        assert artifact.zone_code == zone
        assert parent_section.code == section
        assert parent_zone.code == zone
        assert artifact.is_extension is False


def test_eisf_reverse_mappings_resolve_through_active_catalog():
    """
    Verify that all reverse mappings defined in the eISF adapter successfully
    resolve to standard, valid artifacts, sections, and zones in the active complete catalog.
    """
    from tmf_reference_model import get_active_catalog, resolve_artifact

    from apps.eisf.adapter import REVERSE_MAPPING

    active_catalog = get_active_catalog()
    assert active_catalog.version == "v3.2.0-complete"

    for (zone, section, etmf_art_type, etmf_code), (
        binder_sec,
        art_type,
    ) in REVERSE_MAPPING.items():
        # Resolve artifact dynamically through the taxonomy catalog
        resolved = resolve_artifact(active_catalog.version, code=etmf_code)

        # Verify correctness of the resolution
        artifact = resolved["artifact"]
        parent_section = resolved["section"]
        parent_zone = resolved["zone"]

        assert artifact.code == etmf_code
        assert artifact.section_code == section
        assert artifact.zone_code == zone
        assert parent_section.code == section
        assert parent_zone.code == zone
        assert artifact.is_extension is False


def test_eisf_resolve_known_extension_artifact():
    """
    Verify that resolving a known extension artifact directly from v3.2.0-extended
    results in an artifact with is_extension=True.
    """
    from tmf_reference_model import resolve_artifact

    resolved = resolve_artifact("v3.2.0-extended", code="05.02.99")
    artifact = resolved["artifact"]
    assert artifact.code == "05.02.99"
    assert artifact.is_extension is True
