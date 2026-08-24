"""Comprehensive Unit & Integration Test Suite for SMART on FHIR eSource Ingestion Pipeline and CRC Reconciliation Drawer.

Requirements:
- PRD-ESR-001: SMART Backend Services Automated Ingestion
- PRD-ESR-002: Clinical Research Coordinator (CRC) Reconciliation Drawer
- PRD-ESR-003: Immutable Source Data Provenance & Part 11 Audit Attribution
- PRD-SYS-122: System Baseline, Authentication, and Audit Ledger Rules
- ADR-2199: Architecture Decision Record for SMART on FHIR Ingestion Pipeline & CRC Reconciliation Drawer
- FDA Guidance on Electronic Source Data in Clinical Investigations
"""

import httpx
import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.interop.application.esource_ingestion_service import (
    EsourceIngestionService,
)
from apps.interop.application.esource_reconciliation_service import (
    EsourceReconciliationService,
    ReconciliationError,
)
from apps.interop.application.smart_auth_service import (
    SmartAuthError,
    SmartAuthService,
)
from apps.interop.domain.esource_models import (
    DiscrepancyType,
    EhrEndpointConfig,
    EhrSystemType,
    ReconciliationAction,
    ReconciliationStatus,
)
from apps.interop.infrastructure.database import db_manager
from apps.interop.infrastructure.esource_repository import (
    EsourceReconciliationRepository,
)
from apps.interop.infrastructure.models import (
    Base,
    InteropAuditLog,
)
from apps.interop.main import app
from packages.security.rbac_helpers import build_gateway_headers


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    """Setup in-memory Interop database for unit and integration testing."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_staff_headers(
    user_id: str = "crc_coordinator_01",
    roles: str = "site_coordinator",
    change_reason: str = "CRC eSource Reconciliation Action",
) -> dict[str, str]:
    """Helper to generate valid gateway V2 signed headers for CRC/staff operations."""
    return build_gateway_headers(
        user_id=user_id,
        roles=roles,
        change_reason=change_reason,
        site_id="SITE-01",
    )


# ==============================================================================
# 1. SMART on FHIR Backend Services Asymmetric Authentication Tests
# ==============================================================================


def test_smart_backend_services_asymmetric_jwt_lifecycle():
    """Test asymmetric private key JWT generation and verification per SMART Backend Services specification.

    @req: PRD-SYS-185
    @req: PRD-SYS-186
    @req: PRD-ESR-001
    @req: PRD-SYS-122
    @req: ADR-2199
    """
    auth_service = SmartAuthService()
    priv_pem, pub_pem = auth_service.generate_rsa_key_pair(2048)

    client_id = "cadence-clinical-gateway"
    token_url = "https://hospital-ehr.org/oauth2/token"

    # 1. Generate client assertion
    assertion = auth_service.create_client_assertion(
        client_id=client_id,
        token_endpoint=token_url,
        private_key_pem=priv_pem,
        algorithm="RS384",
        expiration_seconds=300,
    )
    assert assertion is not None
    assert isinstance(assertion, str)
    assert len(assertion.split(".")) == 3

    # 2. Verify assertion claims
    claims = auth_service.verify_client_assertion(
        assertion_jwt=assertion,
        public_key_pem=pub_pem,
        expected_audience=token_url,
        expected_client_id=client_id,
    )
    assert claims["iss"] == client_id
    assert claims["sub"] == client_id
    assert claims["aud"] == token_url
    assert "jti" in claims
    assert claims["exp"] > claims["iat"]
    assert (claims["exp"] - claims["iat"]) <= 300


def test_smart_backend_services_assertion_security_validations():
    """Test security constraints on SMART Backend Services assertions (expiry, audience, replay defense).

    @req: PRD-SYS-185
    @req: PRD-SYS-186
    @req: PRD-ESR-001
    @req: PRD-SYS-122
    @req: ADR-2199
    """
    auth_service = SmartAuthService()
    priv_pem, pub_pem = auth_service.generate_rsa_key_pair(2048)

    client_id = "cadence-gateway-01"
    token_url = "https://ehr.hospital.edu/oauth2/token"

    assertion = auth_service.create_client_assertion(
        client_id=client_id,
        token_endpoint=token_url,
        private_key_pem=priv_pem,
        algorithm="RS384",
        expiration_seconds=120,
    )

    # Audience mismatch
    with pytest.raises(SmartAuthError) as exc_aud:
        auth_service.verify_client_assertion(
            assertion_jwt=assertion,
            public_key_pem=pub_pem,
            expected_audience="https://wrong-ehr.org/token",
        )
    assert "Invalid audience" in exc_aud.value.message

    # Client ID mismatch
    with pytest.raises(SmartAuthError) as exc_cid:
        auth_service.verify_client_assertion(
            assertion_jwt=assertion,
            public_key_pem=pub_pem,
            expected_client_id="different-client-id",
        )
    assert "Client ID mismatch" in exc_cid.value.message

    # Replay attack defense: first verification succeeds, second fails with replay error
    claims1 = auth_service.verify_client_assertion(
        assertion_jwt=assertion,
        public_key_pem=pub_pem,
        expected_audience=token_url,
        expected_client_id=client_id,
    )
    assert claims1["iss"] == client_id

    with pytest.raises(SmartAuthError) as exc_replay:
        auth_service.verify_client_assertion(
            assertion_jwt=assertion,
            public_key_pem=pub_pem,
            expected_audience=token_url,
            expected_client_id=client_id,
        )
    assert "Replay attack detected" in exc_replay.value.message

    # Wrong public key fails signature check
    _, wrong_pub = auth_service.generate_rsa_key_pair(2048)
    new_assertion = auth_service.create_client_assertion(
        client_id=client_id,
        token_endpoint=token_url,
        private_key_pem=priv_pem,
    )
    with pytest.raises(SmartAuthError) as exc_sig:
        auth_service.verify_client_assertion(
            assertion_jwt=new_assertion,
            public_key_pem=wrong_pub,
        )
    assert "signature" in exc_sig.value.message.lower()


@pytest.mark.asyncio
async def test_smart_token_exchange_and_caching():
    """Test OAuth2 client credentials token exchange and in-memory token lifecycle caching.

    @req: PRD-SYS-185
    @req: PRD-SYS-186
    @req: PRD-ESR-001
    @req: ADR-2199
    """
    auth_service = SmartAuthService()
    priv_pem, _ = auth_service.generate_rsa_key_pair(2048)

    endpoint_config = EhrEndpointConfig(
        endpoint_id="EP-001",
        name="Johns Hopkins Hospital",
        system_type=EhrSystemType.EPIC,
        fhir_base_url="https://epic.jhh.edu/r4",
        token_url="https://epic.jhh.edu/oauth2/token",
        client_id="cadence-client-app",
        scopes=["system/Patient.read", "system/Observation.read"],
    )

    # Mock HTTP transport
    mock_token_response = {
        "access_token": "mock-bearer-token-abc-12345",
        "token_type": "Bearer",
        "expires_in": 3600,
        "scope": "system/Patient.read system/Observation.read",
    }

    call_count = 0

    def mock_transport_handler(req: httpx.Request) -> httpx.Response:
        nonlocal call_count
        call_count += 1
        return httpx.Response(200, json=mock_token_response)

    transport = httpx.MockTransport(mock_transport_handler)
    async with httpx.AsyncClient(transport=transport) as client:
        # First retrieval triggers token exchange
        token1 = await auth_service.get_authenticated_access_token(
            endpoint_config=endpoint_config,
            private_key_pem=priv_pem,
            client=client,
        )
        assert token1 == "mock-bearer-token-abc-12345"
        assert call_count == 1

        # Second retrieval uses cache (zero new network calls)
        token2 = await auth_service.get_authenticated_access_token(
            endpoint_config=endpoint_config,
            private_key_pem=priv_pem,
            client=client,
        )
        assert token2 == token1
        assert call_count == 1

        # Force refresh bypasses cache
        token3 = await auth_service.get_authenticated_access_token(
            endpoint_config=endpoint_config,
            private_key_pem=priv_pem,
            client=client,
            force_refresh=True,
        )
        assert token3 == token1
        assert call_count == 2


def test_smart_app_launch_flow_and_pkce_generation():
    """Test RFC 7636 PKCE code challenge generation and SMART on FHIR authorization URL building.

    @req: PRD-SYS-185
    @req: PRD-SYS-186
    @req: PRD-ESR-001
    @req: ADR-2199
    """
    auth_service = SmartAuthService()

    # 1. Generate PKCE pair
    verifier, challenge = auth_service.generate_pkce_pair(64)
    assert len(verifier) == 64
    assert len(challenge) > 0
    assert "=" not in challenge  # Base64url unpadded

    # 2. Build EHR Launch URL
    ehr_auth_url = auth_service.build_smart_launch_authorize_url(
        authorize_endpoint="https://ehr.hospital.org/oauth2/authorize",
        client_id="cadence-app-client",
        redirect_uri="https://cadence.clinical.app/callback",
        scope="launch launch/patient openid fhirUser patient/*.read",
        state="crypto-csrf-state-nonce-123",
        code_challenge=challenge,
        launch_context="launch-ctx-token-ehr-7788",
    )
    assert "response_type=code" in ehr_auth_url
    assert "client_id=cadence-app-client" in ehr_auth_url
    assert "code_challenge=" in ehr_auth_url
    assert "code_challenge_method=S256" in ehr_auth_url
    assert "launch=launch-ctx-token-ehr-7788" in ehr_auth_url

    # 3. Build Standalone Launch URL
    standalone_url = auth_service.build_smart_launch_authorize_url(
        authorize_endpoint="https://ehr.hospital.org/oauth2/authorize",
        client_id="cadence-app-client",
        redirect_uri="https://cadence.clinical.app/callback",
        scope="patient/*.read openid",
        state="state-abc-456",
        code_challenge=challenge,
        aud="https://ehr.hospital.org/fhir/r4",
    )
    assert "aud=https%3A%2F%2Fehr.hospital.org%2Ffhir%2Fr4" in standalone_url


@pytest.mark.asyncio
async def test_smart_oauth2_pkce_token_exchange_and_launch_context():
    """Test OAuth2 PKCE authorization code exchange and SMART launch context metadata extraction.

    @req: PRD-SYS-185
    @req: PRD-SYS-186
    @req: PRD-ESR-001
    @req: ADR-2199
    """
    auth_service = SmartAuthService()
    verifier, _ = auth_service.generate_pkce_pair(64)

    mock_resp = {
        "access_token": "bearer-smart-pkce-access-token-9988",
        "token_type": "Bearer",
        "expires_in": 1800,
        "scope": "launch/patient patient/*.read openid",
        "patient": "PAT-HOSPITAL-5566",
        "encounter": "ENC-HOSPITAL-1122",
        "tenant_id": "TENANT-EAST-HEALTH",
        "need_patient_banner": True,
    }

    def mock_handler(req: httpx.Request) -> httpx.Response:
        form = req.read().decode("utf-8")
        assert "grant_type=authorization_code" in form
        assert "code_verifier=" in form
        assert "code=auth-code-12345" in form
        return httpx.Response(200, json=mock_resp)

    transport = httpx.MockTransport(mock_handler)
    async with httpx.AsyncClient(transport=transport) as client:
        token_resp = await auth_service.exchange_pkce_token(
            token_endpoint="https://ehr.hospital.org/oauth2/token",
            client_id="cadence-app-client",
            code="auth-code-12345",
            code_verifier=verifier,
            redirect_uri="https://cadence.clinical.app/callback",
            client=client,
        )

        assert token_resp.access_token == "bearer-smart-pkce-access-token-9988"
        assert token_resp.patient == "PAT-HOSPITAL-5566"
        assert token_resp.encounter == "ENC-HOSPITAL-1122"
        assert token_resp.tenant_id == "TENANT-EAST-HEALTH"
        assert token_resp.need_patient_banner is True


def test_smart_ehr_tenant_scoping_validation():
    """Test multi-tenant EHR boundary validation and cross-tenant access rejection.

    @req: PRD-SYS-185
    @req: PRD-SYS-186
    @req: PRD-SYS-122
    @req: ADR-2199
    """
    auth_service = SmartAuthService()

    allowed = [
        "https://fhir.hospital-system.org/tenant-a",
        "https://fhir.hospital-system.org/tenant-b",
    ]

    # Valid tenant in whitelist
    assert (
        auth_service.validate_ehr_tenant_scoping(
            issuer_url="https://fhir.hospital-system.org/tenant-a",
            allowed_tenants=allowed,
        )
        is True
    )

    # Valid tenant with matching required tenant ID
    assert (
        auth_service.validate_ehr_tenant_scoping(
            issuer_url="https://fhir.hospital-system.org/tenant-a",
            required_tenant_id="tenant-a",
        )
        is True
    )

    # Invalid tenant not in whitelist
    with pytest.raises(SmartAuthError) as exc_tenant:
        auth_service.validate_ehr_tenant_scoping(
            issuer_url="https://fhir.rogue-hospital.org/tenant-x",
            allowed_tenants=allowed,
        )
    assert "not authorized in tenant whitelist" in exc_tenant.value.message

    # Tenant mismatch
    with pytest.raises(SmartAuthError) as exc_mismatch:
        auth_service.validate_ehr_tenant_scoping(
            issuer_url="https://fhir.hospital-system.org/tenant-a",
            required_tenant_id="tenant-b",
        )
    assert "does not match required tenant ID" in exc_mismatch.value.message


# ==============================================================================
# 2. FHIR Ingestion and CDASH Domain Normalization Tests
# ==============================================================================


def test_fhir_demographics_dm_ingestion_and_pii_stripping():
    """Test FHIR Patient normalization to CDASH DM domain with deterministic pseudonymization and PII stripping.

    @req: PRD-SYS-185
    @req: PRD-SYS-186
    @req: PRD-ESR-001
    @req: PRD-SYS-122
    @req: ADR-2199
    """
    ingest_svc = EsourceIngestionService()

    fhir_bundle = {
        "resourceType": "Bundle",
        "entry": [
            {
                "resource": {
                    "resourceType": "Patient",
                    "id": "PATIENT-998877",
                    "name": [{"family": "Smith", "given": ["Jane"]}],
                    "telecom": [{"system": "phone", "value": "555-0199"}],
                    "address": [{"line": ["742 Evergreen Terrace"]}],
                    "gender": "female",
                    "birthDate": "1988-06-15",
                }
            }
        ],
    }

    candidates = ingest_svc.normalize_fhir_bundle(
        bundle=fhir_bundle,
        study_id="STUDY-201",
        site_id="SITE-01",
        ehr_system_id="EPIC-HEALTH",
    )

    assert len(candidates) >= 3  # SEX, BRTHDTC, AGE

    # Check SEX
    sex_cand = next((c for c in candidates if c.target_variable == "eCRF.DM.SEX"), None)
    assert sex_cand is not None
    assert sex_cand.candidate_value == "F"
    assert sex_cand.target_domain == "DM"
    assert sex_cand.confidence_score == 1.0

    # Check BRTHDTC
    brth_cand = next(
        (c for c in candidates if c.target_variable == "eCRF.DM.BRTHDTC"), None
    )
    assert brth_cand is not None
    assert brth_cand.candidate_value == "1988-06-15"

    # Check AGE
    age_cand = next((c for c in candidates if c.target_variable == "eCRF.DM.AGE"), None)
    assert age_cand is not None
    assert isinstance(age_cand.candidate_value, int)
    assert age_cand.candidate_unit == "YEARS"

    # Verify PII is stripped in raw_fhir_resource
    raw_res = sex_cand.raw_fhir_resource
    assert "name" not in raw_res
    assert "telecom" not in raw_res
    assert "address" not in raw_res


def test_fhir_vital_signs_vs_ingestion_and_multi_components():
    """Test FHIR Observation (vital-signs) normalization to CDASH VS domain, including multi-component panels.

    @req: PRD-SYS-185
    @req: PRD-SYS-186
    @req: PRD-ESR-001
    @req: ADR-2199
    """
    ingest_svc = EsourceIngestionService()

    fhir_bundle = {
        "resourceType": "Bundle",
        "entry": [
            # Heart rate
            {
                "resource": {
                    "resourceType": "Observation",
                    "id": "OBS-HR-01",
                    "status": "final",
                    "category": [
                        {
                            "coding": [
                                {
                                    "system": "http://terminology.hl7.org/CodeSystem/observation-category",
                                    "code": "vital-signs",
                                }
                            ]
                        }
                    ],
                    "code": {
                        "coding": [
                            {
                                "system": "http://loinc.org",
                                "code": "8867-4",
                                "display": "Heart rate",
                            }
                        ],
                        "text": "Pulse Rate",
                    },
                    "effectiveDateTime": "2026-08-20T10:30:00Z",
                    "valueQuantity": {"value": 72, "unit": "beats/min"},
                }
            },
            # Body Temperature
            {
                "resource": {
                    "resourceType": "Observation",
                    "id": "OBS-TEMP-01",
                    "status": "final",
                    "category": [
                        {
                            "coding": [
                                {
                                    "system": "http://terminology.hl7.org/CodeSystem/observation-category",
                                    "code": "vital-signs",
                                }
                            ]
                        }
                    ],
                    "code": {
                        "coding": [
                            {
                                "system": "http://loinc.org",
                                "code": "8310-5",
                                "display": "Body temperature",
                            }
                        ]
                    },
                    "effectiveDateTime": "2026-08-20T10:30:00Z",
                    "valueQuantity": {"value": 36.8, "unit": "C"},
                }
            },
            # Multi-component Blood Pressure Panel
            {
                "resource": {
                    "resourceType": "Observation",
                    "id": "OBS-BP-PANEL",
                    "status": "final",
                    "category": [
                        {
                            "coding": [
                                {
                                    "system": "http://terminology.hl7.org/CodeSystem/observation-category",
                                    "code": "vital-signs",
                                }
                            ]
                        }
                    ],
                    "code": {
                        "coding": [
                            {
                                "system": "http://loinc.org",
                                "code": "85354-9",
                                "display": "Blood pressure panel with all children optional",
                            }
                        ]
                    },
                    "effectiveDateTime": "2026-08-20T10:30:00Z",
                    "component": [
                        {
                            "code": {
                                "coding": [
                                    {
                                        "system": "http://loinc.org",
                                        "code": "8480-6",
                                        "display": "Systolic blood pressure",
                                    }
                                ]
                            },
                            "valueQuantity": {"value": 120, "unit": "mmHg"},
                        },
                        {
                            "code": {
                                "coding": [
                                    {
                                        "system": "http://loinc.org",
                                        "code": "8462-4",
                                        "display": "Diastolic blood pressure",
                                    }
                                ]
                            },
                            "valueQuantity": {"value": 80, "unit": "mmHg"},
                        },
                    ],
                }
            },
        ],
    }

    candidates = ingest_svc.normalize_fhir_bundle(
        bundle=fhir_bundle,
        study_id="STUDY-201",
        site_id="SITE-01",
        ehr_system_id="CERNER-EHR",
    )

    # Verify Pulse
    pulse_cand = next(
        (c for c in candidates if c.target_variable == "eCRF.VS.PULSE"), None
    )
    assert pulse_cand is not None
    assert pulse_cand.candidate_value == 72
    assert pulse_cand.candidate_unit == "beats/min"
    assert pulse_cand.target_domain == "VS"

    # Verify Temperature
    temp_cand = next(
        (c for c in candidates if c.target_variable == "eCRF.VS.TEMP"), None
    )
    assert temp_cand is not None
    assert temp_cand.candidate_value == 36.8

    # Verify Multi-component Systolic & Diastolic BP
    sys_cand = next(
        (c for c in candidates if c.target_variable == "eCRF.VS.SYSBP"), None
    )
    dia_cand = next(
        (c for c in candidates if c.target_variable == "eCRF.VS.DIABP"), None
    )
    assert sys_cand is not None
    assert sys_cand.candidate_value == 120
    assert sys_cand.candidate_unit == "mmHg"
    assert dia_cand is not None
    assert dia_cand.candidate_value == 80
    assert dia_cand.candidate_unit == "mmHg"


def test_fhir_laboratory_lb_and_medications_ingestion():
    """Test FHIR Observation (laboratory), Condition (MH), and MedicationRequest (CM) normalization.

    @req: PRD-SYS-185
    @req: PRD-SYS-186
    @req: PRD-ESR-001
    @req: ADR-2199
    """
    ingest_svc = EsourceIngestionService()

    fhir_bundle = {
        "resourceType": "Bundle",
        "entry": [
            # Glucose Lab
            {
                "resource": {
                    "resourceType": "Observation",
                    "id": "OBS-LAB-GLUC",
                    "status": "final",
                    "code": {
                        "coding": [
                            {
                                "system": "http://loinc.org",
                                "code": "2339-0",
                                "display": "Glucose [Mass/volume] in Blood",
                            }
                        ]
                    },
                    "effectiveDateTime": "2026-08-20T08:00:00Z",
                    "valueQuantity": {"value": 98.5, "unit": "mg/dL"},
                }
            },
            # Condition (Type 2 Diabetes)
            {
                "resource": {
                    "resourceType": "Condition",
                    "id": "COND-DM2",
                    "clinicalStatus": {"coding": [{"code": "active"}]},
                    "code": {
                        "coding": [
                            {
                                "system": "http://snomed.info/sct",
                                "code": "44054006",
                                "display": "Type 2 diabetes mellitus",
                            }
                        ]
                    },
                    "onsetDateTime": "2020-03-15",
                }
            },
            # MedicationRequest (Metformin)
            {
                "resource": {
                    "resourceType": "MedicationRequest",
                    "id": "MED-METFORMIN",
                    "status": "active",
                    "medicationCodeableConcept": {
                        "coding": [
                            {
                                "system": "http://www.nlm.nih.gov/research/umls/rxnorm",
                                "code": "6809",
                                "display": "Metformin hydrochloride 500 MG Oral Tablet",
                            }
                        ],
                        "text": "Metformin 500mg",
                    },
                    "authoredOn": "2021-01-10",
                    "dosageInstruction": [
                        {
                            "doseAndRate": [
                                {"doseQuantity": {"value": 500, "unit": "mg"}}
                            ]
                        }
                    ],
                }
            },
        ],
    }

    candidates = ingest_svc.normalize_fhir_bundle(
        bundle=fhir_bundle,
        study_id="STUDY-201",
        site_id="SITE-01",
        ehr_system_id="EPIC-HOPKINS",
    )

    # Check Lab (LB.GLUC)
    gluc_cand = next(
        (c for c in candidates if c.target_variable == "eCRF.LB.GLUC"), None
    )
    assert gluc_cand is not None
    assert gluc_cand.candidate_value == 98.5
    assert gluc_cand.candidate_unit == "mg/dL"
    assert gluc_cand.target_domain == "LB"

    # Check Condition (MH.MHTERM)
    cond_cand = next(
        (c for c in candidates if c.target_variable == "eCRF.MH.MHTERM"), None
    )
    assert cond_cand is not None
    assert "diabetes" in str(cond_cand.candidate_value).lower()
    assert cond_cand.target_domain == "MH"

    # Check Medication (CM.CMTRT)
    med_cand = next(
        (c for c in candidates if c.target_variable == "eCRF.CM.CMTRT"), None
    )
    assert med_cand is not None
    assert "metformin" in str(med_cand.candidate_value).lower()
    assert med_cand.target_domain == "CM"


def test_fhir_procedure_and_encounter_ingestion():
    """Test FHIR Procedure (PR) and Encounter (SV) normalization into CDASH domain variables.

    @req: PRD-SYS-185
    @req: PRD-SYS-186
    @req: PRD-ESR-001
    @req: ADR-2199
    """
    ingest_svc = EsourceIngestionService()

    fhir_bundle = {
        "resourceType": "Bundle",
        "entry": [
            # Procedure (Appendectomy)
            {
                "resource": {
                    "resourceType": "Procedure",
                    "id": "PROC-APP-01",
                    "status": "completed",
                    "code": {
                        "coding": [
                            {
                                "system": "http://snomed.info/sct",
                                "code": "80146002",
                                "display": "Appendectomy",
                            }
                        ]
                    },
                    "performedDateTime": "2024-05-12T14:30:00Z",
                }
            },
            # Encounter (Screening Visit)
            {
                "resource": {
                    "resourceType": "Encounter",
                    "id": "ENC-VISIT-01",
                    "status": "finished",
                    "class": {
                        "code": "AMB",
                        "display": "Ambulatory Clinic Visit",
                    },
                    "period": {
                        "start": "2026-08-20T09:00:00Z",
                        "end": "2026-08-20T11:00:00Z",
                    },
                }
            },
        ],
    }

    candidates = ingest_svc.normalize_fhir_bundle(
        bundle=fhir_bundle,
        study_id="STUDY-PROC-101",
        site_id="SITE-01",
        ehr_system_id="EPIC-PROC",
    )

    # Check Procedure (PR.PRTRT)
    proc_cand = next((c for c in candidates if c.target_domain == "PR"), None)
    assert proc_cand is not None
    assert proc_cand.target_variable == "eCRF.PR.PRTRT"
    assert "Appendectomy" in str(proc_cand.candidate_value)

    # Check Encounter (SV.VISIT)
    enc_cand = next((c for c in candidates if c.target_domain == "SV"), None)
    assert enc_cand is not None
    assert enc_cand.target_variable == "eCRF.SV.VISIT"
    assert "Ambulatory" in str(enc_cand.candidate_value)


def test_fhir_physical_examination_pe_ingestion():
    """Test FHIR Observation (Physical Exam category) normalization into CDASH PE domain variables.

    @req: PRD-SYS-185
    @req: PRD-SYS-186
    @req: PRD-ESR-001
    @req: ADR-2199
    """
    ingest_svc = EsourceIngestionService()

    fhir_bundle = {
        "resourceType": "Bundle",
        "entry": [
            {
                "resource": {
                    "resourceType": "Observation",
                    "id": "OBS-PE-CV-01",
                    "status": "final",
                    "category": [
                        {
                            "coding": [
                                {
                                    "system": "http://terminology.hl7.org/CodeSystem/observation-category",
                                    "code": "exam",
                                }
                            ]
                        }
                    ],
                    "code": {
                        "coding": [
                            {
                                "system": "http://snomed.info/sct",
                                "code": "301857004",
                                "display": "Cardiovascular examination",
                            }
                        ],
                        "text": "Cardiovascular exam normal",
                    },
                    "effectiveDateTime": "2026-08-20T10:00:00Z",
                    "valueQuantity": {"value": 1.0, "unit": "normal"},
                }
            }
        ],
    }

    candidates = ingest_svc.normalize_fhir_bundle(
        bundle=fhir_bundle,
        study_id="STUDY-PE-101",
        site_id="SITE-01",
        ehr_system_id="EPIC-PE",
    )

    pe_cand = next((c for c in candidates if c.target_domain == "PE"), None)
    assert pe_cand is not None
    assert pe_cand.target_variable == "eCRF.PE.CVEXAM"
    assert pe_cand.cdash_testcd == "CVEXAM"


def test_ecrf_side_by_side_comparison_and_discrepancies():
    """Test discrepancy classification when comparing incoming candidate EHR values against existing eCRF state.

    @req: PRD-SYS-185
    @req: PRD-SYS-186
    @req: PRD-ESR-002
    @req: ADR-2199
    """
    ingest_svc = EsourceIngestionService()

    # Pre-existing eCRF state in EDC
    existing_ecrf = {
        "eCRF.VS.SYSBP": 135,  # Value mismatch (EHR has 120)
        "eCRF.VS.DIABP": 80,  # Exact match (EHR has 80)
        # eCRF.VS.PULSE is missing (New field)
    }

    fhir_bundle = {
        "resourceType": "Bundle",
        "entry": [
            {
                "resource": {
                    "resourceType": "Observation",
                    "id": "OBS-BP",
                    "category": [{"coding": [{"code": "vital-signs"}]}],
                    "code": {"coding": [{"code": "85354-9"}]},
                    "component": [
                        {
                            "code": {"coding": [{"code": "8480-6"}]},
                            "valueQuantity": {"value": 120, "unit": "mmHg"},
                        },
                        {
                            "code": {"coding": [{"code": "8462-4"}]},
                            "valueQuantity": {"value": 80, "unit": "mmHg"},
                        },
                    ],
                }
            },
            {
                "resource": {
                    "resourceType": "Observation",
                    "id": "OBS-HR",
                    "category": [{"coding": [{"code": "vital-signs"}]}],
                    "code": {"coding": [{"code": "8867-4"}]},
                    "valueQuantity": {"value": 70, "unit": "beats/min"},
                }
            },
        ],
    }

    candidates = ingest_svc.normalize_fhir_bundle(
        bundle=fhir_bundle,
        study_id="STUDY-201",
        site_id="SITE-01",
        current_ecrf_context=existing_ecrf,
    )

    sys_cand = next(
        (c for c in candidates if c.target_variable == "eCRF.VS.SYSBP"), None
    )
    dia_cand = next(
        (c for c in candidates if c.target_variable == "eCRF.VS.DIABP"), None
    )
    hr_cand = next(
        (c for c in candidates if c.target_variable == "eCRF.VS.PULSE"), None
    )

    assert sys_cand.discrepancy_type == DiscrepancyType.VALUE_MISMATCH
    assert sys_cand.current_ecrf_value == 135

    assert dia_cand.discrepancy_type == DiscrepancyType.EXACT_MATCH
    assert dia_cand.current_ecrf_value == 80

    assert hr_cand.discrepancy_type == DiscrepancyType.NEW_FIELD
    assert hr_cand.current_ecrf_value is None


# ==============================================================================
# 3. CRC Non-Destructive Reconciliation Workflow & Part 11 Audit Trail Tests
# ==============================================================================


@pytest.mark.asyncio
async def test_crc_reconciliation_workflow_accept_and_commit():
    """Test 1-Click 'Accept & Commit' workflow with electronic signature and GxP audit provenance ledger.

    @req: PRD-SYS-185
    @req: PRD-SYS-186
    @req: PRD-ESR-002
    @req: PRD-ESR-003
    @req: PRD-SYS-122
    @req: ADR-2199
    """
    ingest_svc = EsourceIngestionService()
    reconcile_svc = EsourceReconciliationService()

    fhir_bundle = {
        "resourceType": "Bundle",
        "entry": [
            {
                "resource": {
                    "resourceType": "Observation",
                    "id": "OBS-VITAL-SYS",
                    "category": [{"coding": [{"code": "vital-signs"}]}],
                    "code": {
                        "coding": [
                            {"code": "8480-6", "display": "Systolic Blood Pressure"}
                        ]
                    },
                    "valueQuantity": {"value": 118, "unit": "mmHg"},
                    "effectiveDateTime": "2026-08-21T09:00:00Z",
                }
            }
        ],
    }

    candidates = ingest_svc.normalize_fhir_bundle(
        bundle=fhir_bundle,
        study_id="STUDY-101",
        site_id="SITE-01",
        ehr_system_id="EPIC-MAIN",
    )

    async with db_manager.session_maker() as session:
        repo = EsourceReconciliationRepository(session)
        saved = await reconcile_svc.save_candidates(
            repository=repo, candidates=candidates, user_id="crc_user_1"
        )
        assert len(saved) == 1
        cand_id = saved[0].id
        assert saved[0].status == ReconciliationStatus.PENDING_REVIEW.value

        # Execute Accept & Commit
        updated = await reconcile_svc.accept_and_commit(
            repository=repo,
            candidate_id=cand_id,
            user_id="crc_coordinator_01",
            user_role="site_coordinator",
            signature_token="sig-jwt-token-part11-accept-commit",
            commit_reason="Verified against clinical vitals chart",
        )
        assert updated.status == ReconciliationStatus.ACCEPTED.value
        assert updated.resolution_action == ReconciliationAction.ACCEPT_COMMIT.value
        assert updated.resolved_by == "crc_coordinator_01"
        assert updated.electronic_signature is not None
        assert (
            updated.electronic_signature["token"]
            == "sig-jwt-token-part11-accept-commit"
        )

        # Verify GxP Audit Log Entry
        stmt = select(InteropAuditLog).where(
            InteropAuditLog.action == "ESOURCE_ACCEPT_COMMIT"
        )
        audit_res = await session.execute(stmt)
        audit_entry = audit_res.scalars().first()
        assert audit_entry is not None
        assert audit_entry.user_id == "crc_coordinator_01"
        assert "EPIC-MAIN" in audit_entry.details
        assert "OBS-VITAL-SYS" in audit_entry.details


@pytest.mark.asyncio
async def test_crc_reconciliation_workflow_reject_with_reason():
    """Test 1-Click 'Reject with Reason' enforcing mandatory coordinator justification under GxP.

    @req: PRD-SYS-185
    @req: PRD-SYS-186
    @req: PRD-ESR-002
    @req: PRD-ESR-003
    @req: ADR-2199
    """
    ingest_svc = EsourceIngestionService()
    reconcile_svc = EsourceReconciliationService()

    fhir_bundle = {
        "resourceType": "Bundle",
        "entry": [
            {
                "resource": {
                    "resourceType": "Observation",
                    "id": "OBS-VITAL-DIA",
                    "category": [{"coding": [{"code": "vital-signs"}]}],
                    "code": {
                        "coding": [
                            {"code": "8462-4", "display": "Diastolic Blood Pressure"}
                        ]
                    },
                    "valueQuantity": {"value": 95, "unit": "mmHg"},
                }
            }
        ],
    }

    candidates = ingest_svc.normalize_fhir_bundle(
        bundle=fhir_bundle, study_id="STUDY-101"
    )

    async with db_manager.session_maker() as session:
        repo = EsourceReconciliationRepository(session)
        saved = await reconcile_svc.save_candidates(
            repository=repo, candidates=candidates
        )
        cand_id = saved[0].id

        # Rejection without reason fails
        with pytest.raises(ReconciliationError) as exc_reason:
            await reconcile_svc.reject_candidate(
                repository=repo,
                candidate_id=cand_id,
                user_id="crc_user_01",
                user_role="site_coordinator",
                rejection_reason="",
            )
        assert "rationale is required" in exc_reason.value.message.lower()

        # Rejection with reason succeeds
        updated = await reconcile_svc.reject_candidate(
            repository=repo,
            candidate_id=cand_id,
            user_id="crc_user_01",
            user_role="site_coordinator",
            rejection_reason="Duplicate measurement taken outside protocol window",
        )
        assert updated.status == ReconciliationStatus.REJECTED.value
        assert updated.resolution_action == ReconciliationAction.REJECT.value
        assert (
            updated.resolution_reason
            == "Duplicate measurement taken outside protocol window"
        )

        # Verify Audit Log
        stmt = select(InteropAuditLog).where(InteropAuditLog.action == "ESOURCE_REJECT")
        audit_res = await session.execute(stmt)
        audit_entry = audit_res.scalars().first()
        assert audit_entry is not None
        assert "Duplicate measurement" in audit_entry.details


@pytest.mark.asyncio
async def test_crc_reconciliation_workflow_raise_clinical_query():
    """Test 1-Click 'Raise Clinical Query' creating an open discrepancy in the study query ledger.

    @req: PRD-SYS-185
    @req: PRD-SYS-186
    @req: PRD-ESR-002
    @req: PRD-ESR-003
    @req: ADR-2199
    """
    ingest_svc = EsourceIngestionService()
    reconcile_svc = EsourceReconciliationService()

    fhir_bundle = {
        "resourceType": "Bundle",
        "entry": [
            {
                "resource": {
                    "resourceType": "Observation",
                    "id": "OBS-LAB-K",
                    "code": {
                        "coding": [{"code": "2823-3", "display": "Potassium in Serum"}]
                    },
                    "valueQuantity": {"value": 6.8, "unit": "mmol/L"},
                }
            }
        ],
    }

    candidates = ingest_svc.normalize_fhir_bundle(
        bundle=fhir_bundle, study_id="STUDY-101"
    )

    async with db_manager.session_maker() as session:
        repo = EsourceReconciliationRepository(session)
        saved = await reconcile_svc.save_candidates(
            repository=repo, candidates=candidates
        )
        cand_id = saved[0].id

        updated, query = await reconcile_svc.raise_clinical_query(
            repository=repo,
            candidate_id=cand_id,
            user_id="crc_user_01",
            user_role="site_coordinator",
            query_explanation="Critical potassium spike observed. Please confirm if redraw was performed or specimen hemolyzed.",
            discrepancy_type="VALUE_MISMATCH",
        )

        assert updated.status == ReconciliationStatus.QUERIED.value
        assert updated.clinical_query_id == query.id
        assert query.status == "OPEN"
        assert query.study_id == "STUDY-101"
        assert "Critical potassium spike" in query.explanation

        # Verify Audit Log
        stmt = select(InteropAuditLog).where(
            InteropAuditLog.action == "ESOURCE_QUERY_RAISED"
        )
        audit_res = await session.execute(stmt)
        audit_entry = audit_res.scalars().first()
        assert audit_entry is not None
        assert query.id in audit_entry.details


@pytest.mark.asyncio
async def test_non_destructive_review_guarantee_no_silent_overwrites():
    """Test non-destructive eSource review guarantee: data is never merged into eCRFs without explicit CRC approval.

    @req: PRD-SYS-185
    @req: PRD-SYS-186
    @req: PRD-ESR-002
    @req: PRD-ESR-003
    @req: ADR-2199
    @req: FDA Guidance
    """
    ingest_svc = EsourceIngestionService()
    reconcile_svc = EsourceReconciliationService()

    fhir_bundle = {
        "resourceType": "Bundle",
        "entry": [
            {
                "resource": {
                    "resourceType": "Observation",
                    "id": "OBS-WEIGHT-01",
                    "category": [{"coding": [{"code": "vital-signs"}]}],
                    "code": {"coding": [{"code": "29463-7", "display": "Body Weight"}]},
                    "valueQuantity": {"value": 75.5, "unit": "kg"},
                }
            }
        ],
    }

    candidates = ingest_svc.normalize_fhir_bundle(
        bundle=fhir_bundle, study_id="STUDY-101"
    )

    async with db_manager.session_maker() as session:
        repo = EsourceReconciliationRepository(session)
        saved = await reconcile_svc.save_candidates(
            repository=repo, candidates=candidates
        )
        cand_id = saved[0].id

        # Candidate is created exclusively in PENDING_REVIEW status
        assert saved[0].status == ReconciliationStatus.PENDING_REVIEW.value

        # Re-accepting already accepted or rejected record is rejected
        await reconcile_svc.accept_and_commit(
            repository=repo, candidate_id=cand_id, user_id="crc_1", user_role="crc"
        )

        with pytest.raises(ReconciliationError) as exc_recommit:
            await reconcile_svc.accept_and_commit(
                repository=repo, candidate_id=cand_id, user_id="crc_2", user_role="crc"
            )
        assert (
            "Cannot accept candidate in status 'ACCEPTED'" in exc_recommit.value.message
        )


# ==============================================================================
# 4. REST API Endpoint Integration Tests
# ==============================================================================


def test_esource_rest_api_e2e_endpoints():
    """Test full eSource REST API suite (endpoint registration, bundle ingest, listing, accept, reject, query, provenance).

    @req: PRD-SYS-185
    @req: PRD-SYS-186
    @req: PRD-ESR-001
    @req: PRD-ESR-002
    @req: PRD-ESR-003
    @req: PRD-SYS-122
    @req: ADR-2199
    """
    client = TestClient(app)
    headers = get_staff_headers(user_id="coordinator_mary", roles="site_coordinator")

    # 1. Register EHR Endpoint
    ep_payload = {
        "name": "Massachusetts General Hospital EHR",
        "system_type": "EPIC",
        "fhir_base_url": "https://fhir.mgh.harvard.edu/r4",
        "token_url": "https://fhir.mgh.harvard.edu/oauth2/token",
        "client_id": "cadence-mgh-app",
        "scopes": ["system/Patient.read", "system/Observation.read"],
        "polling_interval_sec": 300,
        "is_active": True,
    }
    ep_resp = client.post(
        "/api/v1/interop/esource/endpoints", json=ep_payload, headers=headers
    )
    assert ep_resp.status_code == 201
    ep_data = ep_resp.json()
    assert ep_data["name"] == "Massachusetts General Hospital EHR"
    assert ep_data["endpoint_id"] is not None

    # 2. List EHR Endpoints
    list_ep_resp = client.get("/api/v1/interop/esource/endpoints", headers=headers)
    assert list_ep_resp.status_code == 200
    assert len(list_ep_resp.json()) >= 1

    # 3. Generate and Verify SMART Client Assertion
    auth_service = SmartAuthService()
    priv_pem, pub_pem = auth_service.generate_rsa_key_pair(2048)

    assert_req = {
        "client_id": "cadence-mgh-app",
        "token_endpoint": "https://fhir.mgh.harvard.edu/oauth2/token",
        "private_key_pem": priv_pem,
        "algorithm": "RS384",
        "expiration_seconds": 180,
    }
    assert_resp = client.post(
        "/api/v1/interop/esource/smart/assertion", json=assert_req, headers=headers
    )
    assert assert_resp.status_code == 200
    assertion_jwt = assert_resp.json()["client_assertion_jwt"]

    verify_req = {
        "assertion_jwt": assertion_jwt,
        "public_key_pem": pub_pem,
        "expected_audience": "https://fhir.mgh.harvard.edu/oauth2/token",
        "expected_client_id": "cadence-mgh-app",
    }
    verify_resp = client.post(
        "/api/v1/interop/esource/smart/verify", json=verify_req, headers=headers
    )
    assert verify_resp.status_code == 200
    assert verify_resp.json()["is_valid"] is True

    # 4. Ingest FHIR Bundle via API
    ingest_payload = {
        "study_id": "STUDY-ONCO-101",
        "site_id": "SITE-MGH-01",
        "ehr_system_id": "MGH-EPIC",
        "bundle": {
            "resourceType": "Bundle",
            "entry": [
                {
                    "resource": {
                        "resourceType": "Patient",
                        "id": "PAT-MGH-881",
                        "gender": "male",
                        "birthDate": "1975-11-20",
                    }
                },
                {
                    "resource": {
                        "resourceType": "Observation",
                        "id": "OBS-MGH-SYSBP",
                        "category": [{"coding": [{"code": "vital-signs"}]}],
                        "code": {
                            "coding": [
                                {"code": "8480-6", "display": "Systolic Blood Pressure"}
                            ]
                        },
                        "valueQuantity": {"value": 128, "unit": "mmHg"},
                        "effectiveDateTime": "2026-08-22T14:00:00Z",
                    }
                },
                {
                    "resource": {
                        "resourceType": "Observation",
                        "id": "OBS-MGH-GLUC",
                        "code": {
                            "coding": [{"code": "2339-0", "display": "Blood Glucose"}]
                        },
                        "valueQuantity": {"value": 105, "unit": "mg/dL"},
                        "effectiveDateTime": "2026-08-22T14:00:00Z",
                    }
                },
            ],
        },
    }
    ingest_resp = client.post(
        "/api/v1/interop/esource/ingest", json=ingest_payload, headers=headers
    )
    assert ingest_resp.status_code == 200
    ingest_data = ingest_resp.json()
    assert ingest_data["total_ingested"] >= 3
    candidate_items = ingest_data["candidates"]

    sysbp_cand = next(
        (c for c in candidate_items if c["target_variable"] == "eCRF.VS.SYSBP"), None
    )
    gluc_cand = next(
        (c for c in candidate_items if c["target_variable"] == "eCRF.LB.GLUC"), None
    )
    sex_cand = next(
        (c for c in candidate_items if c["target_variable"] == "eCRF.DM.SEX"), None
    )
    assert sysbp_cand is not None
    assert gluc_cand is not None
    assert sex_cand is not None

    # 5. List Candidates with Domain & Status Filter
    list_cand_resp = client.get(
        "/api/v1/interop/esource/candidates?study_id=STUDY-ONCO-101&domain=VS&status=PENDING_REVIEW",
        headers=headers,
    )
    assert list_cand_resp.status_code == 200
    vs_list = list_cand_resp.json()["candidates"]
    assert len(vs_list) >= 1
    assert all(c["target_domain"] == "VS" for c in vs_list)

    # 6. 1-Click Accept & Commit on SYSBP
    accept_req = {
        "candidate_id": sysbp_cand["id"],
        "commit_reason": "Verified systolic BP against clinical monitor",
    }
    accept_resp = client.post(
        "/api/v1/interop/esource/reconcile/accept", json=accept_req, headers=headers
    )
    assert accept_resp.status_code == 200
    assert accept_resp.json()["success"] is True
    assert accept_resp.json()["candidate"]["status"] == "ACCEPTED"

    # 7. 1-Click Reject with Reason on GLUC
    reject_req = {
        "candidate_id": gluc_cand["id"],
        "rejection_reason": "Non-fasting glucose measurement",
    }
    reject_resp = client.post(
        "/api/v1/interop/esource/reconcile/reject", json=reject_req, headers=headers
    )
    assert reject_resp.status_code == 200
    assert reject_resp.json()["success"] is True
    assert reject_resp.json()["candidate"]["status"] == "REJECTED"

    # 8. 1-Click Raise Clinical Query on SEX
    query_req = {
        "candidate_id": sex_cand["id"],
        "query_explanation": "Discrepancy with paper consent form indicating different gender identity.",
    }
    query_resp = client.post(
        "/api/v1/interop/esource/reconcile/query", json=query_req, headers=headers
    )
    assert query_resp.status_code == 200
    assert query_resp.json()["success"] is True
    assert query_resp.json()["candidate"]["status"] == "QUERIED"
    assert query_resp.json()["clinical_query_id"] is not None

    # 9. Get Provenance & Part 11 Audit Trail
    prov_resp = client.get(
        f"/api/v1/interop/esource/provenance/{sysbp_cand['id']}", headers=headers
    )
    assert prov_resp.status_code == 200
    prov_data = prov_resp.json()
    assert prov_data["candidate_id"] == sysbp_cand["id"]
    assert prov_data["ehr_system_id"] == "MGH-EPIC"
    assert prov_data["fhir_resource_type"] == "Observation"
    assert prov_data["target_variable"] == "eCRF.VS.SYSBP"
    assert prov_data["status"] == "ACCEPTED"
    assert len(prov_data["audit_trail"]) >= 1


@pytest.mark.asyncio
async def test_automated_ehr_polling_pipeline():
    """Test automated hospital EHR polling against mock FHIR server endpoints with SMART authentication.

    @req: PRD-SYS-185
    @req: PRD-SYS-186
    @req: PRD-ESR-001
    @req: ADR-2199
    """
    auth_service = SmartAuthService()
    priv_pem, _ = auth_service.generate_rsa_key_pair(2048)
    ingest_svc = EsourceIngestionService(smart_auth=auth_service)

    endpoint_config = EhrEndpointConfig(
        endpoint_id="EP-MAYO-01",
        name="Mayo Clinic EHR",
        system_type=EhrSystemType.EPIC,
        fhir_base_url="https://fhir.mayo.edu/r4",
        token_url="https://fhir.mayo.edu/oauth2/token",
        client_id="cadence-mayo-integration",
        scopes=["system/Patient.read", "system/Observation.read"],
    )

    def mock_fhir_server(request: httpx.Request) -> httpx.Response:
        url_str = str(request.url)
        # Token endpoint
        if "oauth2/token" in url_str:
            return httpx.Response(
                200,
                json={
                    "access_token": "bearer-token-mayo-9988",
                    "token_type": "Bearer",
                    "expires_in": 3600,
                    "scope": "system/Patient.read system/Observation.read",
                },
            )
        # Patient endpoint
        if "/Patient/PAT-001" in url_str:
            return httpx.Response(
                200,
                json={
                    "resourceType": "Patient",
                    "id": "PAT-001",
                    "gender": "female",
                    "birthDate": "1992-04-12",
                },
            )
        # Vital signs
        if "category=vital-signs" in url_str:
            return httpx.Response(
                200,
                json={
                    "resourceType": "Bundle",
                    "entry": [
                        {
                            "resource": {
                                "resourceType": "Observation",
                                "id": "OBS-MAY-PULSE",
                                "category": [{"coding": [{"code": "vital-signs"}]}],
                                "code": {
                                    "coding": [
                                        {"code": "8867-4", "display": "Heart rate"}
                                    ]
                                },
                                "valueQuantity": {"value": 68, "unit": "beats/min"},
                                "effectiveDateTime": "2026-08-23T11:00:00Z",
                            }
                        }
                    ],
                },
            )
        # Laboratory
        if "category=laboratory" in url_str:
            return httpx.Response(
                200,
                json={
                    "resourceType": "Bundle",
                    "entry": [
                        {
                            "resource": {
                                "resourceType": "Observation",
                                "id": "OBS-MAY-HGB",
                                "code": {
                                    "coding": [
                                        {
                                            "code": "718-7",
                                            "display": "Hemoglobin [Mass/volume] in Blood",
                                        }
                                    ]
                                },
                                "valueQuantity": {"value": 14.2, "unit": "g/dL"},
                                "effectiveDateTime": "2026-08-23T11:00:00Z",
                            }
                        }
                    ],
                },
            )
        # Condition
        if "/Condition?" in url_str:
            return httpx.Response(
                200,
                json={
                    "resourceType": "Bundle",
                    "entry": [
                        {
                            "resource": {
                                "resourceType": "Condition",
                                "id": "COND-MAY-ASTHMA",
                                "clinicalStatus": {"coding": [{"code": "active"}]},
                                "code": {
                                    "coding": [
                                        {"code": "195967001", "display": "Asthma"}
                                    ]
                                },
                                "onsetDateTime": "2015-06-01",
                            }
                        }
                    ],
                },
            )
        # MedicationRequest
        if "/MedicationRequest?" in url_str:
            return httpx.Response(
                200,
                json={
                    "resourceType": "Bundle",
                    "entry": [
                        {
                            "resource": {
                                "resourceType": "MedicationRequest",
                                "id": "MED-MAY-ALBUTEROL",
                                "medicationCodeableConcept": {
                                    "text": "Albuterol Inhaler 90mcg"
                                },
                                "authoredOn": "2022-03-01",
                            }
                        }
                    ],
                },
            )
        return httpx.Response(404, json={"detail": "Not found"})

    transport = httpx.MockTransport(mock_fhir_server)
    async with httpx.AsyncClient(transport=transport) as client:
        candidates = await ingest_svc.poll_hospital_ehr(
            endpoint_config=endpoint_config,
            private_key_pem=priv_pem,
            study_id="STUDY-MAYO-01",
            site_id="SITE-01",
            subject_id="PAT-001",
            client=client,
        )

        assert len(candidates) >= 5
        domains = {c.target_domain for c in candidates}
        assert "DM" in domains
        assert "VS" in domains
        assert "LB" in domains
        assert "MH" in domains
        assert "CM" in domains

        pulse = next(
            (c for c in candidates if c.target_variable == "eCRF.VS.PULSE"), None
        )
        assert pulse is not None
        assert pulse.candidate_value == 68
