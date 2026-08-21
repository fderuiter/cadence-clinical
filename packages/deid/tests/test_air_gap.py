"""Unit test suite for in-flight de-identification air-gap engine and surrogate token vault.

Requirements: PRD-SYS-051
"""

from packages.deid.air_gap import DeidAirGapVault
from packages.deid.models import ComplianceProfile


def test_deidentify_and_rehydrate_basic_phi() -> None:
    """Validate in-flight prompt de-identification and in-memory re-hydration.

    Requirements: PRD-SYS-051
    """
    raw_prompt = (
        "Patient Alice Smith with SSN 123-45-6789 and MRN: 98765432 "
        "was admitted on 2026-05-12. Contact: alice.smith@example.org or 555-123-4567."
    )

    with DeidAirGapVault() as vault:
        sanitized = vault.deidentify_text(
            text=raw_prompt,
            profile=ComplianceProfile.HIPAA,
            custom_terms=["Alice Smith"],
        )

        assert "123-45-6789" not in sanitized
        assert "98765432" not in sanitized
        assert "alice.smith@example.org" not in sanitized
        assert "555-123-4567" not in sanitized
        assert "Alice Smith" not in sanitized
        assert "[SURROGATE_" in sanitized
        assert vault.has_surrogates is True
        assert vault.surrogate_count >= 5

        # Simulate model completion referencing the surrogate tokens
        simulated_model_output = (
            f"Summary for {vault.raw_to_surrogate['Alice Smith']} "
            f"(SSN: {vault.raw_to_surrogate['123-45-6789']}): Patient condition is stable."
        )

        rehydrated = vault.rehydrate_text(simulated_model_output)
        assert "Alice Smith" in rehydrated
        assert "123-45-6789" in rehydrated
        assert "[SURROGATE_" not in rehydrated


def test_co_reference_consistency_across_messages() -> None:
    """Validate that identical identifiers share the same surrogate token across turns.

    Requirements: PRD-SYS-051
    """
    messages = [
        {
            "role": "user",
            "content": "Patient John Doe (SSN: 000-11-2222) reported symptoms.",
        },
        {
            "role": "assistant",
            "content": "Reviewing chart for John Doe.",
        },
        {
            "role": "user",
            "content": "Can you verify if 000-11-2222 has any drug allergies for John Doe?",
        },
    ]

    with DeidAirGapVault() as vault:
        sanitized_messages = vault.deidentify_messages(
            messages=messages,
            profile=ComplianceProfile.HIPAA,
            custom_terms=["John Doe"],
        )

        assert len(sanitized_messages) == 3
        # Ensure all 3 messages are sanitized
        for msg in sanitized_messages:
            assert "John Doe" not in msg["content"]
            assert "000-11-2222" not in msg["content"]

        # Ensure consistent surrogate tokens
        john_surrogate = vault.raw_to_surrogate["John Doe"]
        ssn_surrogate = vault.raw_to_surrogate["000-11-2222"]

        assert john_surrogate in sanitized_messages[0]["content"]
        assert ssn_surrogate in sanitized_messages[0]["content"]
        assert john_surrogate in sanitized_messages[1]["content"]
        assert john_surrogate in sanitized_messages[2]["content"]
        assert ssn_surrogate in sanitized_messages[2]["content"]


def test_rehydrate_nested_structured_data() -> None:
    """Validate recursive re-hydration of structured JSON output objects.

    Requirements: PRD-SYS-051
    """
    with DeidAirGapVault() as vault:
        # Register surrogates
        s_name = vault.get_or_create_surrogate("Jane Doe", "CUSTOM")
        s_mrn = vault.get_or_create_surrogate("MRN-554433", "MEDICAL_RECORD_ACCOUNT")
        s_date = vault.get_or_create_surrogate("2026-06-15", "DATES")

        model_json = {
            "subject_name": s_name,
            "patient_mrn": s_mrn,
            "admission_date": s_date,
            "age": 45,
            "is_active": True,
            "events": [
                {"description": f"Initial triage for {s_name}", "code": "E-101"},
                {"description": f"Discharge on {s_date}", "code": "E-102"},
            ],
            "metadata": {"notes": f"Verified by chart {s_mrn}"},
        }

        rehydrated = vault.rehydrate_structured_data(model_json)

        assert rehydrated["subject_name"] == "Jane Doe"
        assert rehydrated["patient_mrn"] == "MRN-554433"
        assert rehydrated["admission_date"] == "2026-06-15"
        assert rehydrated["age"] == 45
        assert rehydrated["is_active"] is True
        assert rehydrated["events"][0]["description"] == "Initial triage for Jane Doe"
        assert rehydrated["events"][1]["description"] == "Discharge on 2026-06-15"
        assert rehydrated["metadata"]["notes"] == "Verified by chart MRN-554433"


def test_deidentify_batch_texts_embeddings() -> None:
    """Validate batch text sanitization for vector embeddings generation.

    Requirements: PRD-SYS-051
    """
    input_texts = [
        "Adverse event note for patient Bob Smith (SSN: 999-88-7777).",
        "Concomitant medication log for Bob Smith on 2026-01-10.",
    ]

    with DeidAirGapVault() as vault:
        sanitized = vault.deidentify_texts(
            texts=input_texts,
            profile=ComplianceProfile.HIPAA,
            custom_terms=["Bob Smith"],
        )

        assert len(sanitized) == 2
        assert "Bob Smith" not in sanitized[0]
        assert "999-88-7777" not in sanitized[0]
        assert "Bob Smith" not in sanitized[1]
        assert vault.has_surrogates is True


def test_vault_lifecycle_and_cleanup() -> None:
    """Validate ephemeral memory purge on vault exit.

    Requirements: PRD-SYS-051
    """
    vault = DeidAirGapVault()
    vault.deidentify_text("Patient SSN: 111-22-3333", custom_terms=[])
    assert vault.has_surrogates is True
    assert vault.surrogate_count > 0

    vault.clear()
    assert vault.has_surrogates is False
    assert vault.surrogate_count == 0
    assert len(vault.surrogate_to_raw) == 0
    assert len(vault.raw_to_surrogate) == 0


def test_no_phi_noop() -> None:
    """Validate that text without PHI is unaltered and no surrogates are allocated.

    Requirements: PRD-SYS-051
    """
    clean_text = (
        "Standard clinical trial protocol synopsis describing primary endpoints."
    )
    with DeidAirGapVault() as vault:
        sanitized = vault.deidentify_text(clean_text)
        assert sanitized == clean_text
        assert vault.has_surrogates is False
        assert vault.surrogate_count == 0
