import os
import uuid

import pytest
import pytest_asyncio

from apps.execution.biostat import (
    DatasetJSONValidationError,
    serialize_to_dataset_json,
    validate_dataset_json,
)
from apps.execution.biostat.terminology import (
    normalize_race,
    normalize_seriousness,
    normalize_severity,
    normalize_sex,
)
from apps.execution.database.core import db_manager
from apps.execution.database.models import Base, TranslationJob
from apps.execution.translator import process_translation


@pytest_asyncio.fixture(autouse=True)
async def setup_test_db():
    db_manager.init_db(
        os.getenv(
            "TEST_DATABASE_URL",
            "sqlite+aiosqlite:///:memory:",
        )
    )
    async with db_manager.engine.begin() as conn:
        from sqlalchemy import text

        if db_manager.engine.dialect.name == "postgresql":
            await conn.execute(text("CREATE SCHEMA IF NOT EXISTS audit_schema;"))
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def test_terminology_numeric_and_boolean_mappings():
    """Verify standard mapping of numeric and boolean codes to CDISC terms."""
    # Sex normalization checks
    assert normalize_sex("1") == "M"
    assert normalize_sex("2") == "F"
    assert normalize_sex("9") == "U"
    assert normalize_sex("M") == "M"
    assert normalize_sex("F") == "F"
    assert normalize_sex("U") == "U"

    # Race normalization checks
    assert normalize_race("White") == "WHITE"
    assert normalize_race(["ASIAN", "WHITE"]) == "MULTIPLE"

    # Severity normalization checks
    assert normalize_severity("1") == "MILD"
    assert normalize_severity("2") == "MODERATE"
    assert normalize_severity("3") == "SEVERE"

    # Seriousness normalization checks
    assert normalize_seriousness(True) == "Y"
    assert normalize_seriousness(False) == "N"
    assert normalize_seriousness("YES") == "Y"
    assert normalize_seriousness("NO") == "N"
    assert normalize_seriousness("1") == "Y"
    assert normalize_seriousness("0") == "N"
    assert normalize_seriousness(1) == "Y"
    assert normalize_seriousness(0) == "N"


def test_validator_with_variant_and_boolean_inputs():
    """Verify that dataset validator correctly validates and accepts numeric gender and boolean seriousness."""
    valid_bundle = {
        "DM": [
            {
                "STUDYID": "STUDY-002",
                "DOMAIN": "DM",
                "USUBJID": "STUDY-002-SUBJ-01",
                "SUBJID": "01",
                "SEX": "1",  # Numeric sex which normalizes to 'M'
                "RACE": "WHITE",
                "ARM": "Active",
            }
        ],
        "AE": [
            {
                "STUDYID": "STUDY-002",
                "DOMAIN": "AE",
                "USUBJID": "STUDY-002-SUBJ-01",
                "AESEQ": 1,
                "AETERM": "Nausea",
                "AESER": True,  # Boolean seriousness which maps to 'Y'
                "AESEV": "1",  # Numeric severity which maps to 'MILD'
            }
        ],
    }

    dj = serialize_to_dataset_json(data=valid_bundle, study_id="STUDY-002")
    # This should pass without raising DatasetJSONValidationError
    validate_dataset_json(dj)


def test_validator_with_invalid_gender():
    """Verify validator fails on completely invalid genders."""
    invalid_bundle = {
        "DM": [
            {
                "STUDYID": "STUDY-002",
                "DOMAIN": "DM",
                "USUBJID": "STUDY-002-SUBJ-01",
                "SUBJID": "01",
                "SEX": "INVALID_VALUE",
                "RACE": "WHITE",
                "ARM": "Active",
            }
        ]
    }

    dj = serialize_to_dataset_json(data=invalid_bundle, study_id="STUDY-002")
    with pytest.raises(DatasetJSONValidationError) as exc:
        validate_dataset_json(dj)
    assert "CONTROLLED_TERMINOLOGY_VIOLATION" in str(exc.value)


@pytest.mark.asyncio
async def test_translation_fails_early_on_invalid_structure():
    """Verify that translation background worker blocks target XML compiling and fails early if structural errors exist."""
    # Payload with duplicate physical IDs
    invalid_study_payload = {
        "id": "00000000-0000-0000-0000-000000000001",
        "name": "Invalid Duplicates Study",
        "protocol": {
            "items": [
                {"id": "item1", "name": "Item 1", "type": "int"},
            ]
        },
        # Trigger duplicate element ID check by reusing the study ID in version
        "versions": [
            {
                "id": "00000000-0000-0000-0000-000000000001",  # DUPLICATE with root study ID!
                "versionIdentifier": "1.0",
                "rationale": "Initial Version",
                "studyIdentifiers": [],
                "titles": [],
                "instanceType": "StudyVersion",
                "studyDesigns": [],
            }
        ],
    }

    job_id = str(uuid.uuid4())
    session_factory = db_manager.get_session_maker()

    # We expect process_translation to run and raise ValueError/fail early due to validation failure.
    # It catches errors inside and writes FAILED status to the TranslationJob record.
    await process_translation(
        study_id="00000000-0000-0000-0000-000000000001",
        payload=invalid_study_payload,
        session_factory=session_factory,
        job_id=job_id,
    )

    # Verify that the translation job is marked as FAILED with a clear error message
    async with session_factory() as session:
        result = await session.execute(
            TranslationJob.__table__.select().where(TranslationJob.id == job_id)
        )
        job = result.mappings().first()
        assert job is not None
        assert job["status"] == "FAILED"
        assert "Validation Failed" in job["error_message"]
        assert "Duplicate physical ID" in job["error_message"]
        assert job["odm_payload"] is None
        assert job["openrosa_payload"] is None
