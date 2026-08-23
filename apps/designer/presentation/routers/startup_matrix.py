"""FastAPI router for Country-Aware Startup Prerequisite Rules Matrix endpoints in Study Designer.

Requirements: PRD-SSU-001, PRD-SYS-121, ADR-2199, ICH GCP E6(R2) Section 8.
"""

from typing import Any

from fastapi import APIRouter, Depends
from pydantic import BaseModel, Field

from packages.security.middleware import get_current_user

router = APIRouter(
    prefix="/api/v1/designer/startup-matrix", tags=["StartupPrerequisiteMatrix"]
)

DEFAULT_JURISDICTION_PRESETS: dict[str, list[dict[str, Any]]] = {
    "US": [
        {
            "facet": "REGULATORY_IRB",
            "criterion_code": "FDA_1572",
            "criterion_name": "Form FDA 1572 Statement of Investigator",
            "is_mandatory": True,
            "description": "21 CFR 312.60 signed Form FDA 1572",
        },
        {
            "facet": "REGULATORY_IRB",
            "criterion_code": "LOCAL_IRB_APPROVAL",
            "criterion_name": "Institutional Review Board (IRB) Approval",
            "is_mandatory": True,
            "description": "21 CFR 56 / ICH GCP 4.4 approval letter",
        },
        {
            "facet": "REGULATORY_IRB",
            "criterion_code": "FINANCIAL_DISCLOSURE",
            "criterion_name": "Financial Disclosure Form (FDF)",
            "is_mandatory": False,
            "description": "21 CFR 54 Financial Disclosure",
        },
        {
            "facet": "CTA_BUDGET",
            "criterion_code": "SITE_CONTRACT",
            "criterion_name": "Executed Clinical Trial Agreement (CTA)",
            "is_mandatory": True,
            "description": "Fully executed site contract and budget schedule",
        },
        {
            "facet": "EISF_BASELINE",
            "criterion_code": "EISF_BASELINE_CERT",
            "criterion_name": "Certified eISF Regulatory Binder Baseline",
            "is_mandatory": True,
            "description": "Baseline investigator CV, medical licenses, and lab certifications",
        },
        {
            "facet": "DOA_LOG",
            "criterion_code": "DOA_LOG",
            "criterion_name": "PI Signed Delegation of Authority (DOA) Log",
            "is_mandatory": True,
            "description": "ICH GCP E6(R2) 4.1.5 signed site delegation log",
        },
        {
            "facet": "STAFF_TRAINING",
            "criterion_code": "GCP_TRAINING",
            "criterion_name": "GCP & Protocol Amendment Training",
            "is_mandatory": True,
            "description": "ICH GCP E6(R2) Section 4 staff training verification",
        },
        {
            "facet": "IP_DEPOT_RELEASE",
            "criterion_code": "IP_RELEASE_AUTHORIZATION",
            "criterion_name": "Investigational Product Depot Greenlight Release",
            "is_mandatory": True,
            "description": "Depot release authorization for investigational drug shipment",
        },
    ],
    "EU": [
        {
            "facet": "REGULATORY_IRB",
            "criterion_code": "EU_CTR_PART_I",
            "criterion_name": "EU CTR Part I Authorisation",
            "is_mandatory": True,
            "description": "Regulation (EU) No 536/2014 Part I scientific assessment approval",
        },
        {
            "facet": "REGULATORY_IRB",
            "criterion_code": "EU_CTR_PART_II_EC",
            "criterion_name": "EU CTR Part II & Ethics Committee Decision",
            "is_mandatory": True,
            "description": "Member State Part II ethical decision and site suitability",
        },
        {
            "facet": "CTA_BUDGET",
            "criterion_code": "SITE_CONTRACT",
            "criterion_name": "Executed Clinical Trial Agreement (CTA)",
            "is_mandatory": True,
            "description": "National institutional clinical trial agreement",
        },
        {
            "facet": "EISF_BASELINE",
            "criterion_code": "EISF_BASELINE_CERT",
            "criterion_name": "Certified eISF Regulatory Binder Baseline",
            "is_mandatory": True,
            "description": "Certified investigator dossiers and site facilities",
        },
        {
            "facet": "DOA_LOG",
            "criterion_code": "DOA_LOG",
            "criterion_name": "PI Signed Delegation of Authority (DOA) Log",
            "is_mandatory": True,
            "description": "Delegation of trial tasks log",
        },
        {
            "facet": "STAFF_TRAINING",
            "criterion_code": "GCP_TRAINING",
            "criterion_name": "GCP & Protocol Training",
            "is_mandatory": True,
            "description": "GCP and protocol version training certificates",
        },
        {
            "facet": "IP_DEPOT_RELEASE",
            "criterion_code": "IP_RELEASE_AUTHORIZATION",
            "criterion_name": "Investigational Product Depot Greenlight Release",
            "is_mandatory": True,
            "description": "QP release and depot greenlight release authorization",
        },
    ],
    "JP": [
        {
            "facet": "REGULATORY_IRB",
            "criterion_code": "PMDA_NOTIFICATION",
            "criterion_name": "PMDA Clinical Trial Notification (CTN)",
            "is_mandatory": True,
            "description": "Japan PMDA notification and 30-day review completion",
        },
        {
            "facet": "REGULATORY_IRB",
            "criterion_code": "LOCAL_IRB_APPROVAL",
            "criterion_name": "Institutional Review Board (IRB) Approval",
            "is_mandatory": True,
            "description": "Local medical institution IRB approval certificate",
        },
        {
            "facet": "CTA_BUDGET",
            "criterion_code": "SITE_CONTRACT",
            "criterion_name": "Executed Site Agreement",
            "is_mandatory": True,
            "description": "Tripartite / site clinical trial contract",
        },
        {
            "facet": "EISF_BASELINE",
            "criterion_code": "EISF_BASELINE_CERT",
            "criterion_name": "Certified eISF Regulatory Binder Baseline",
            "is_mandatory": True,
            "description": "Investigator CVs and local lab reference ranges",
        },
        {
            "facet": "DOA_LOG",
            "criterion_code": "DOA_LOG",
            "criterion_name": "PI Signed Delegation of Authority (DOA) Log",
            "is_mandatory": True,
            "description": "Trial responsibility delegation record",
        },
        {
            "facet": "STAFF_TRAINING",
            "criterion_code": "GCP_TRAINING",
            "criterion_name": "J-GCP & Protocol Training",
            "is_mandatory": True,
            "description": "Japanese GCP training verification",
        },
        {
            "facet": "IP_DEPOT_RELEASE",
            "criterion_code": "IP_RELEASE_AUTHORIZATION",
            "criterion_name": "Investigational Product Depot Greenlight Release",
            "is_mandatory": True,
            "description": "Release confirmation for local depot dispatch",
        },
    ],
    "GB": [
        {
            "facet": "REGULATORY_IRB",
            "criterion_code": "MHRA_AUTHORIZATION",
            "criterion_name": "MHRA Clinical Trial Authorisation (CTA)",
            "is_mandatory": True,
            "description": "UK MHRA Clinical Trial Authorisation",
        },
        {
            "facet": "REGULATORY_IRB",
            "criterion_code": "UK_REC_APPROVAL",
            "criterion_name": "HRA & Research Ethics Committee (REC) Approval",
            "is_mandatory": True,
            "description": "Health Research Authority & REC favorable opinion",
        },
        {
            "facet": "CTA_BUDGET",
            "criterion_code": "SITE_CONTRACT",
            "criterion_name": "model Clinical Trial Agreement (mCTA)",
            "is_mandatory": True,
            "description": "UK NHS model Clinical Trial Agreement",
        },
        {
            "facet": "EISF_BASELINE",
            "criterion_code": "EISF_BASELINE_CERT",
            "criterion_name": "Certified eISF Regulatory Binder Baseline",
            "is_mandatory": True,
            "description": "Investigator credentials and NHS site readiness",
        },
        {
            "facet": "DOA_LOG",
            "criterion_code": "DOA_LOG",
            "criterion_name": "PI Signed Delegation of Authority (DOA) Log",
            "is_mandatory": True,
            "description": "Delegation of authority record",
        },
        {
            "facet": "STAFF_TRAINING",
            "criterion_code": "GCP_TRAINING",
            "criterion_name": "UK GCP & Protocol Training",
            "is_mandatory": True,
            "description": "NIHR GCP training and protocol amendment training",
        },
        {
            "facet": "IP_DEPOT_RELEASE",
            "criterion_code": "IP_RELEASE_AUTHORIZATION",
            "criterion_name": "Investigational Product Depot Greenlight Release",
            "is_mandatory": True,
            "description": "UK QP release and depot distribution authorization",
        },
    ],
    "GLOBAL": [
        {
            "facet": "REGULATORY_IRB",
            "criterion_code": "REGULATORY_APPROVAL",
            "criterion_name": "Regulatory Authority Approval",
            "is_mandatory": True,
            "description": "National competent authority authorization",
        },
        {
            "facet": "REGULATORY_IRB",
            "criterion_code": "LOCAL_IRB_APPROVAL",
            "criterion_name": "Ethics Committee / IRB Approval",
            "is_mandatory": True,
            "description": "Independent Ethics Committee favorable opinion",
        },
        {
            "facet": "CTA_BUDGET",
            "criterion_code": "SITE_CONTRACT",
            "criterion_name": "Executed Clinical Trial Agreement",
            "is_mandatory": True,
            "description": "Fully executed site agreement",
        },
        {
            "facet": "EISF_BASELINE",
            "criterion_code": "EISF_BASELINE_CERT",
            "criterion_name": "Certified eISF Baseline",
            "is_mandatory": True,
            "description": "Baseline essential documents",
        },
        {
            "facet": "DOA_LOG",
            "criterion_code": "DOA_LOG",
            "criterion_name": "PI Signed Delegation of Authority Log",
            "is_mandatory": True,
            "description": "Delegation log",
        },
        {
            "facet": "STAFF_TRAINING",
            "criterion_code": "GCP_TRAINING",
            "criterion_name": "GCP & Protocol Training",
            "is_mandatory": True,
            "description": "Training verification",
        },
        {
            "facet": "IP_DEPOT_RELEASE",
            "criterion_code": "IP_RELEASE_AUTHORIZATION",
            "criterion_name": "IP Depot Greenlight Release",
            "is_mandatory": True,
            "description": "Depot release authorization",
        },
    ],
}


class JurisdictionInfo(BaseModel):
    code: str
    name: str
    regulatory_framework: str
    ethics_framework: str
    mandatory_criteria_count: int


class GenerateStartupTemplateRequest(BaseModel):
    study_id: str = Field(..., description="Study Identifier")
    country_codes: list[str] = Field(
        ..., description="List of target country codes (e.g. ['US', 'EU', 'JP', 'GB'])"
    )


class PrerequisiteCriterionTemplate(BaseModel):
    country_code: str
    facet: str
    criterion_code: str
    criterion_name: str
    is_mandatory: bool
    description: str | None = None


class GenerateStartupTemplateResponse(BaseModel):
    study_id: str
    generated_rules: list[PrerequisiteCriterionTemplate]
    summary_by_country: dict[str, int]


@router.get("/presets", response_model=dict[str, list[dict[str, Any]]])
async def get_startup_presets_endpoint(
    current_user: dict = Depends(get_current_user),
) -> dict[str, list[dict[str, Any]]]:
    """Retrieves standard statutory startup criteria presets across international jurisdictions."""
    return DEFAULT_JURISDICTION_PRESETS


@router.get("/jurisdictions", response_model=list[JurisdictionInfo])
async def get_supported_jurisdictions_endpoint(
    current_user: dict = Depends(get_current_user),
) -> list[JurisdictionInfo]:
    """Lists supported international jurisdictions with regulatory and ethics governance frameworks."""
    return [
        JurisdictionInfo(
            code="US",
            name="United States (FDA)",
            regulatory_framework="21 CFR Part 312 / Form FDA 1572",
            ethics_framework="21 CFR Part 56 (IRB)",
            mandatory_criteria_count=sum(
                1 for c in DEFAULT_JURISDICTION_PRESETS["US"] if c["is_mandatory"]
            ),
        ),
        JurisdictionInfo(
            code="EU",
            name="European Union (EMA / EU CTR)",
            regulatory_framework="Regulation (EU) No 536/2014 Part I",
            ethics_framework="EU CTR Part II / Member State Ethics Committees",
            mandatory_criteria_count=sum(
                1 for c in DEFAULT_JURISDICTION_PRESETS["EU"] if c["is_mandatory"]
            ),
        ),
        JurisdictionInfo(
            code="JP",
            name="Japan (PMDA / MHLW)",
            regulatory_framework="Pharmaceuticals and Medical Devices Act (PMD Act) / J-GCP",
            ethics_framework="Institutional Review Board (IRB)",
            mandatory_criteria_count=sum(
                1 for c in DEFAULT_JURISDICTION_PRESETS["JP"] if c["is_mandatory"]
            ),
        ),
        JurisdictionInfo(
            code="GB",
            name="United Kingdom (MHRA / HRA)",
            regulatory_framework="Medicines for Human Use (Clinical Trials) Regulations",
            ethics_framework="Health Research Authority (HRA) & REC",
            mandatory_criteria_count=sum(
                1 for c in DEFAULT_JURISDICTION_PRESETS["GB"] if c["is_mandatory"]
            ),
        ),
        JurisdictionInfo(
            code="GLOBAL",
            name="Global Default (ICH GCP)",
            regulatory_framework="ICH GCP E6(R2) Section 8",
            ethics_framework="Independent Ethics Committee (IEC)",
            mandatory_criteria_count=sum(
                1 for c in DEFAULT_JURISDICTION_PRESETS["GLOBAL"] if c["is_mandatory"]
            ),
        ),
    ]


@router.post("/generate-template", response_model=GenerateStartupTemplateResponse)
async def generate_startup_template_endpoint(
    payload: GenerateStartupTemplateRequest,
    current_user: dict = Depends(get_current_user),
) -> GenerateStartupTemplateResponse:
    """Generates country-specific startup prerequisite criteria matrix for study protocol design."""
    generated_rules: list[PrerequisiteCriterionTemplate] = []
    summary_by_country: dict[str, int] = {}

    for cc in payload.country_codes:
        norm_cc = cc.upper()
        preset = DEFAULT_JURISDICTION_PRESETS.get(
            norm_cc, DEFAULT_JURISDICTION_PRESETS["GLOBAL"]
        )
        count = 0
        for item in preset:
            generated_rules.append(
                PrerequisiteCriterionTemplate(
                    country_code=norm_cc,
                    facet=item["facet"],
                    criterion_code=item["criterion_code"],
                    criterion_name=item["criterion_name"],
                    is_mandatory=item["is_mandatory"],
                    description=item.get("description"),
                )
            )
            count += 1
        summary_by_country[norm_cc] = count

    return GenerateStartupTemplateResponse(
        study_id=payload.study_id,
        generated_rules=generated_rules,
        summary_by_country=summary_by_country,
    )
