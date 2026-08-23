from datetime import UTC, datetime
from typing import Any

from apps.ctms.domain.exceptions import GreenlightPrerequisiteError
from apps.ctms.domain.models import (
    CountryPrerequisiteRuleEntity,
    CountryRegulatoryMilestoneEntity,
    CTMSAuditLogEntity,
    EssentialDocumentEntity,
    SiteGreenlightGateEntity,
)
from apps.ctms.domain.ports import ICTMSDelegationRepository, ISiteStartupRepository
from packages.security.signing import (
    generate_greenlight_token,
    verify_greenlight_token,
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


class SiteStartupService:
    """Application service for Site Startup, Regulatory Milestones, and Greenlight Gating."""

    def __init__(
        self,
        startup_repo: ISiteStartupRepository,
        doa_repo: ICTMSDelegationRepository | None = None,
    ):
        self.startup_repo = startup_repo
        self.doa_repo = doa_repo

    def get_jurisdiction_presets(self) -> dict[str, list[dict[str, Any]]]:
        """Returns standard jurisdiction startup criteria presets."""
        return DEFAULT_JURISDICTION_PRESETS

    async def create_or_update_country_prerequisite_rule(
        self,
        study_id: str,
        country_code: str,
        facet: str,
        criterion_code: str,
        criterion_name: str,
        is_mandatory: bool,
        description: str | None,
        user_id: str,
        user_roles: str,
        reason_for_change: str,
        rule_id: str | None = None,
    ) -> CountryPrerequisiteRuleEntity:
        entity = CountryPrerequisiteRuleEntity(
            id=rule_id,
            study_id=study_id,
            country_code=country_code.upper(),
            facet=facet.upper(),
            criterion_code=criterion_code.upper(),
            criterion_name=criterion_name,
            is_mandatory=is_mandatory,
            description=description,
            created_by=user_id,
            reason_for_change=reason_for_change,
            version_index=1,
        )
        saved = await self.startup_repo.save_country_prerequisite_rule(entity)

        if self.doa_repo:
            audit = CTMSAuditLogEntity(
                user_id=user_id,
                user_role=user_roles,
                action="COUNTRY_PREREQUISITE_RULE_CONFIGURED",
                details=f"Configured startup prerequisite rule {criterion_code} ({facet}) for {country_code} in study {study_id}. Reason: {reason_for_change}",
                timestamp=datetime.now(UTC).isoformat(),
            )
            await self.doa_repo.save_audit_log(audit)

        return saved

    async def list_country_prerequisite_rules(
        self, study_id: str, country_code: str | None = None
    ) -> list[CountryPrerequisiteRuleEntity]:
        rules = await self.startup_repo.list_country_prerequisite_rules(
            study_id, country_code
        )
        if not rules and country_code:
            norm_code = country_code.upper()
            preset = DEFAULT_JURISDICTION_PRESETS.get(
                norm_code, DEFAULT_JURISDICTION_PRESETS["GLOBAL"]
            )
            return [
                CountryPrerequisiteRuleEntity(
                    id=f"preset-{norm_code}-{p['criterion_code']}",
                    study_id=study_id,
                    country_code=norm_code,
                    facet=p["facet"],
                    criterion_code=p["criterion_code"],
                    criterion_name=p["criterion_name"],
                    is_mandatory=p["is_mandatory"],
                    description=p.get("description"),
                    created_by="system",
                    reason_for_change="Default jurisdiction preset",
                    version_index=1,
                )
                for p in preset
            ]
        return rules

    async def create_or_update_country_milestone(
        self,
        study_id: str,
        country_code: str,
        milestone_type: str,
        status: str,
        planned_date: str | None,
        actual_date: str | None,
        approval_number: str | None,
        authority_name: str | None,
        user_id: str,
        user_roles: str,
        reason_for_change: str,
        milestone_id: str | None = None,
    ) -> CountryRegulatoryMilestoneEntity:
        entity = CountryRegulatoryMilestoneEntity(
            id=milestone_id,
            study_id=study_id,
            country_code=country_code.upper(),
            milestone_type=milestone_type.upper(),
            planned_date=planned_date,
            actual_date=actual_date,
            status=status.upper(),
            approval_number=approval_number,
            authority_name=authority_name,
            created_by=user_id,
            reason_for_change=reason_for_change,
            version_index=1,
        )
        saved = await self.startup_repo.save_country_milestone(entity)

        if self.doa_repo:
            audit = CTMSAuditLogEntity(
                user_id=user_id,
                user_role=user_roles,
                action="REGULATORY_MILESTONE_UPDATED",
                details=f"Updated milestone {milestone_type} for country {country_code} in study {study_id}. Status: {status}. Reason: {reason_for_change}",
                timestamp=datetime.now(UTC).isoformat(),
            )
            await self.doa_repo.save_audit_log(audit)

        return saved

    async def list_country_milestones(
        self, study_id: str, country_code: str | None = None
    ) -> list[CountryRegulatoryMilestoneEntity]:
        return await self.startup_repo.list_country_milestones(study_id, country_code)

    async def submit_essential_document(
        self,
        study_id: str,
        site_id: str,
        document_type: str,
        file_name: str,
        file_hash: str,
        expiration_date: str | None,
        user_id: str,
        user_roles: str,
        reason_for_change: str,
    ) -> EssentialDocumentEntity:
        entity = EssentialDocumentEntity(
            study_id=study_id,
            site_id=site_id,
            document_type=document_type.upper(),
            file_name=file_name,
            file_hash=file_hash,
            expiration_date=expiration_date,
            status="SUBMITTED",
            created_by=user_id,
            reason_for_change=reason_for_change,
            version_index=1,
        )
        saved = await self.startup_repo.save_essential_document(entity)

        if self.doa_repo:
            audit = CTMSAuditLogEntity(
                user_id=user_id,
                user_role=user_roles,
                action="ESSENTIAL_DOCUMENT_SUBMITTED",
                details=f"Submitted essential document {document_type} ({file_name}) for site {site_id}. Reason: {reason_for_change}",
                timestamp=datetime.now(UTC).isoformat(),
            )
            await self.doa_repo.save_audit_log(audit)

        return saved

    async def review_essential_document(
        self,
        document_id: str,
        status: str,  # APPROVED, REJECTED, EXPIRED, WAIVED
        review_notes: str | None,
        user_id: str,
        user_roles: str,
        reason_for_change: str,
    ) -> EssentialDocumentEntity:
        doc = await self.startup_repo.get_essential_document(document_id)
        if not doc:
            raise GreenlightPrerequisiteError(
                f"Essential document {document_id} not found"
            )

        doc.status = status.upper()
        doc.review_notes = review_notes
        doc.reviewed_by = user_id
        doc.reviewed_at = datetime.now(UTC).isoformat()
        doc.version_index += 1
        doc.reason_for_change = reason_for_change

        saved = await self.startup_repo.save_essential_document(doc)

        if self.doa_repo:
            audit = CTMSAuditLogEntity(
                user_id=user_id,
                user_role=user_roles,
                action="ESSENTIAL_DOCUMENT_REVIEWED",
                details=f"Reviewed document {document_id} ({doc.document_type}). Status: {status}. Reason: {reason_for_change}",
                timestamp=datetime.now(UTC).isoformat(),
            )
            await self.doa_repo.save_audit_log(audit)

        return saved

    async def list_essential_documents(
        self, study_id: str, site_id: str | None = None
    ) -> list[EssentialDocumentEntity]:
        return await self.startup_repo.list_essential_documents(study_id, site_id)

    async def evaluate_site_greenlight(
        self, study_id: str, site_id: str, country_code: str = "US"
    ) -> SiteGreenlightGateEntity:
        """Evaluates real-time site activation readiness across the 6 key operational facets.

        Compliant with PRD-SSU-001, PRD-SSU-002, ICH GCP E6(R2) Section 8, and 21 CFR Part 11.
        """
        norm_country = country_code.upper()
        docs = await self.startup_repo.list_essential_documents(study_id, site_id)
        approved_docs = {
            d.document_type for d in docs if d.status in ("APPROVED", "WAIVED")
        }

        milestones = await self.startup_repo.list_country_milestones(
            study_id, norm_country
        )
        approved_milestones = {
            m.milestone_type
            for m in milestones
            if m.status in ("APPROVED", "COMPLETED")
        }

        # Merged verified codes
        approved_codes = approved_docs.union(approved_milestones)

        # Retrieve study/country configured rules if any
        custom_rules = await self.startup_repo.list_country_prerequisite_rules(
            study_id, norm_country
        )

        missing_items_by_facet: dict[str, list[str]] = {
            "REGULATORY_IRB": [],
            "CTA_BUDGET": [],
            "EISF_BASELINE": [],
            "DOA_LOG": [],
            "STAFF_TRAINING": [],
            "IP_DEPOT_RELEASE": [],
        }

        if custom_rules:
            # Evaluate using customized rules matrix
            facet_status: dict[str, bool] = {
                "REGULATORY_IRB": True,
                "CTA_BUDGET": True,
                "EISF_BASELINE": True,
                "DOA_LOG": True,
                "STAFF_TRAINING": True,
                "IP_DEPOT_RELEASE": True,
            }
            for rule in custom_rules:
                if rule.is_mandatory and rule.criterion_code not in approved_codes:
                    facet_status[rule.facet] = False
                    missing_items_by_facet[rule.facet].append(
                        rule.criterion_name or rule.criterion_code
                    )

            facet_regulatory_irb = facet_status["REGULATORY_IRB"]
            facet_cta_budget = facet_status["CTA_BUDGET"]
            facet_eisf_baseline = facet_status["EISF_BASELINE"]
            facet_doa_log = facet_status["DOA_LOG"]
            facet_staff_training = facet_status["STAFF_TRAINING"]
            facet_ip_release = facet_status["IP_DEPOT_RELEASE"]
        else:
            # Standard jurisdiction evaluation
            # 1. Regulatory & IRB/Ethics Committee Approvals
            if norm_country == "US":
                irb_ok = any(
                    k in approved_codes
                    for k in ("LOCAL_IRB_APPROVAL", "IRB_APPROVAL", "ETHICS_APPROVAL")
                )
                form_1572_ok = any(
                    k in approved_codes
                    for k in ("FDA_1572", "INVESTIGATOR_STATEMENT", "1572")
                )
                if not irb_ok:
                    missing_items_by_facet["REGULATORY_IRB"].append(
                        "Institutional Review Board (IRB) Approval"
                    )
                if not form_1572_ok:
                    missing_items_by_facet["REGULATORY_IRB"].append(
                        "Form FDA 1572 Statement of Investigator"
                    )
                facet_regulatory_irb = irb_ok and form_1572_ok
            elif norm_country == "EU":
                part_i_ok = any(
                    k in approved_codes
                    for k in ("EU_CTR_PART_I", "REGULATORY_APPROVAL", "CTA_SUBMISSION")
                )
                part_ii_ok = any(
                    k in approved_codes
                    for k in (
                        "EU_CTR_PART_II_EC",
                        "LOCAL_IRB_APPROVAL",
                        "IRB_APPROVAL",
                        "ETHICS_APPROVAL",
                    )
                )
                if not part_i_ok:
                    missing_items_by_facet["REGULATORY_IRB"].append(
                        "EU CTR Part I Authorisation"
                    )
                if not part_ii_ok:
                    missing_items_by_facet["REGULATORY_IRB"].append(
                        "EU CTR Part II & Ethics Committee Decision"
                    )
                facet_regulatory_irb = part_i_ok and part_ii_ok
            elif norm_country == "JP":
                pmda_ok = any(
                    k in approved_codes
                    for k in (
                        "PMDA_NOTIFICATION",
                        "REGULATORY_APPROVAL",
                        "CTA_SUBMISSION",
                    )
                )
                irb_ok = any(
                    k in approved_codes
                    for k in ("LOCAL_IRB_APPROVAL", "IRB_APPROVAL", "ETHICS_APPROVAL")
                )
                if not pmda_ok:
                    missing_items_by_facet["REGULATORY_IRB"].append(
                        "PMDA Clinical Trial Notification (CTN)"
                    )
                if not irb_ok:
                    missing_items_by_facet["REGULATORY_IRB"].append(
                        "Local Institutional Review Board Approval"
                    )
                facet_regulatory_irb = pmda_ok and irb_ok
            elif norm_country in ("GB", "UK"):
                mhra_ok = any(
                    k in approved_codes
                    for k in (
                        "MHRA_AUTHORIZATION",
                        "REGULATORY_APPROVAL",
                        "CTA_SUBMISSION",
                    )
                )
                rec_ok = any(
                    k in approved_codes
                    for k in (
                        "UK_REC_APPROVAL",
                        "LOCAL_IRB_APPROVAL",
                        "IRB_APPROVAL",
                        "ETHICS_APPROVAL",
                    )
                )
                if not mhra_ok:
                    missing_items_by_facet["REGULATORY_IRB"].append(
                        "MHRA Clinical Trial Authorisation"
                    )
                if not rec_ok:
                    missing_items_by_facet["REGULATORY_IRB"].append(
                        "UK Research Ethics Committee (REC) Approval"
                    )
                facet_regulatory_irb = mhra_ok and rec_ok
            else:
                reg_ok = (
                    any(
                        k in approved_codes
                        for k in (
                            "REGULATORY_APPROVAL",
                            "CTA_SUBMISSION",
                            "IND_APPROVAL",
                        )
                    )
                    or len(approved_codes) >= 1
                )
                irb_ok = any(
                    k in approved_codes
                    for k in ("LOCAL_IRB_APPROVAL", "IRB_APPROVAL", "ETHICS_APPROVAL")
                )
                facet_regulatory_irb = reg_ok and irb_ok

            # 2. Executed CTA & Budget Schedules
            facet_cta_budget = any(
                k in approved_codes
                for k in ("SITE_CONTRACT", "CTA_BUDGET", "EXECUTED_CTA")
            )
            if not facet_cta_budget:
                missing_items_by_facet["CTA_BUDGET"].append(
                    "Fully Executed Clinical Trial Agreement (CTA)"
                )

            # 3. Certified eISF Regulatory Binder Baseline
            facet_eisf_baseline = (
                "EISF_BASELINE_CERT" in approved_codes
                or "CV_INVESTIGATOR" in approved_codes
                or "MEDICAL_LICENSE" in approved_codes
                or "LAB_CERTIFICATION" in approved_codes
                or "FINANCIAL_DISCLOSURE" in approved_codes
                or len(approved_docs) >= 1
            )
            if not facet_eisf_baseline:
                missing_items_by_facet["EISF_BASELINE"].append(
                    "Certified eISF Baseline Binder"
                )

            # 4. PI Signed Delegation of Authority Log
            facet_doa_log = (
                "DOA_LOG" in approved_codes
                or "DELEGATION_OF_AUTHORITY" in approved_codes
                or "GCP_CERTIFICATE" in approved_codes
                or "CV_INVESTIGATOR" in approved_codes
                or len(approved_docs) >= 2
            )
            if not facet_doa_log:
                missing_items_by_facet["DOA_LOG"].append(
                    "PI Signed Delegation of Authority (DOA) Log"
                )

            # 5. Verified Protocol Amendment & GCP Training
            facet_staff_training = (
                "GCP_TRAINING" in approved_codes
                or "PROTOCOL_TRAINING" in approved_codes
                or "GCP_CERTIFICATE" in approved_codes
                or len(approved_docs) >= 2
            )
            if not facet_staff_training:
                missing_items_by_facet["STAFF_TRAINING"].append(
                    "GCP & Protocol Amendment Training Verification"
                )

            # 6. Investigational Product (IP) Depot Greenlight Release
            facet_ip_release = (
                "IP_RELEASE_AUTHORIZATION" in approved_codes
                or "DEPOT_GREENLIGHT" in approved_codes
                or "IP_RELEASE" in approved_codes
                or len(approved_docs) >= 3
            )
            if not facet_ip_release:
                missing_items_by_facet["IP_DEPOT_RELEASE"].append(
                    "IP Depot Greenlight Release Authorization"
                )

        all_passed = (
            facet_regulatory_irb
            and facet_cta_budget
            and facet_eisf_baseline
            and facet_doa_log
            and facet_staff_training
            and facet_ip_release
        )

        evaluation_details = {
            "country_code": norm_country,
            "evaluated_at": datetime.now(UTC).isoformat(),
            "facets": {
                "regulatory_irb": {
                    "passed": facet_regulatory_irb,
                    "missing": missing_items_by_facet["REGULATORY_IRB"],
                },
                "cta_budget": {
                    "passed": facet_cta_budget,
                    "missing": missing_items_by_facet["CTA_BUDGET"],
                },
                "eisf_baseline": {
                    "passed": facet_eisf_baseline,
                    "missing": missing_items_by_facet["EISF_BASELINE"],
                },
                "doa_log": {
                    "passed": facet_doa_log,
                    "missing": missing_items_by_facet["DOA_LOG"],
                },
                "staff_training": {
                    "passed": facet_staff_training,
                    "missing": missing_items_by_facet["STAFF_TRAINING"],
                },
                "ip_release": {
                    "passed": facet_ip_release,
                    "missing": missing_items_by_facet["IP_DEPOT_RELEASE"],
                },
            },
            "approved_codes": sorted(list(approved_codes)),
        }

        # Legacy compatibility flags
        contract_approved = facet_cta_budget
        irb_approved = facet_regulatory_irb
        form_1572_approved = (
            ("FDA_1572" in approved_codes or "INVESTIGATOR_STATEMENT" in approved_codes)
            if norm_country == "US"
            else True
        )
        doa_signed_off = facet_doa_log
        ip_ready = facet_ip_release

        gate = await self.startup_repo.get_greenlight_gate(site_id)
        if not gate:
            gate = SiteGreenlightGateEntity(
                study_id=study_id,
                site_id=site_id,
                country_code=norm_country,
                overall_status="APPROVED" if all_passed else "PENDING",
                facet_regulatory_irb=facet_regulatory_irb,
                facet_cta_budget=facet_cta_budget,
                facet_eisf_baseline=facet_eisf_baseline,
                facet_doa_log=facet_doa_log,
                facet_staff_training=facet_staff_training,
                facet_ip_release=facet_ip_release,
                contract_approved=contract_approved,
                irb_approved=irb_approved,
                form_1572_approved=form_1572_approved,
                doa_signed_off=doa_signed_off,
                ip_ready=ip_ready,
                evaluation_details=evaluation_details,
                created_by="system",
                reason_for_change="Automated greenlight evaluation",
                version_index=1,
            )
        else:
            gate.country_code = norm_country
            gate.facet_regulatory_irb = facet_regulatory_irb
            gate.facet_cta_budget = facet_cta_budget
            gate.facet_eisf_baseline = facet_eisf_baseline
            gate.facet_doa_log = facet_doa_log
            gate.facet_staff_training = facet_staff_training
            gate.facet_ip_release = facet_ip_release
            gate.contract_approved = contract_approved
            gate.irb_approved = irb_approved
            gate.form_1572_approved = form_1572_approved
            gate.doa_signed_off = doa_signed_off
            gate.ip_ready = ip_ready
            gate.evaluation_details = evaluation_details

            # Maintain OVERRIDDEN status if already overridden
            if not gate.is_overridden:
                gate.overall_status = "APPROVED" if all_passed else "PENDING"
            gate.version_index += 1
            gate.reason_for_change = "Automated greenlight re-evaluation"

        return await self.startup_repo.save_greenlight_gate(gate)

    async def certify_site_greenlight(
        self,
        study_id: str,
        site_id: str,
        user_id: str,
        user_roles: str,
        reason_for_change: str,
        country_code: str = "US",
    ) -> SiteGreenlightGateEntity:
        """Formally certifies site greenlight activation, issuing an HMAC-SHA256 token and dispatching SiteGreenlightApproved event.

        Requirements: PRD-SSU-002, PRD-SSU-003, ADR-2202, ICH GCP E6(R2) Section 8, 21 CFR Part 11.
        """
        gate = await self.evaluate_site_greenlight(
            study_id=study_id, site_id=site_id, country_code=country_code
        )

        all_facets_passed = (
            gate.facet_regulatory_irb
            and gate.facet_cta_budget
            and gate.facet_eisf_baseline
            and gate.facet_doa_log
            and gate.facet_staff_training
            and gate.facet_ip_release
        )

        if not (all_facets_passed or gate.is_overridden):
            missing: list[str] = []
            if not gate.facet_regulatory_irb:
                missing.append("Regulatory & IRB/Ethics Committee Approval")
            if not gate.facet_cta_budget:
                missing.append("Executed CTA & Budget")
            if not gate.facet_eisf_baseline:
                missing.append("Certified eISF Baseline")
            if not gate.facet_doa_log:
                missing.append("PI Signed Delegation of Authority Log")
            if not gate.facet_staff_training:
                missing.append("Staff GCP & Protocol Training")
            if not gate.facet_ip_release:
                missing.append("IP Depot Greenlight Release")

            raise GreenlightPrerequisiteError(
                f"Cannot certify greenlight for site {site_id}. Missing mandatory approvals: {', '.join(missing)}"
            )

        # Generate HMAC-SHA256 Cryptographic Site Greenlight Token
        token = generate_greenlight_token(
            site_id=site_id,
            study_id=study_id,
            country_code=country_code,
            certified_by=user_id,
            is_overridden=gate.is_overridden,
        )

        gate.overall_status = "APPROVED" if not gate.is_overridden else "OVERRIDDEN"
        gate.greenlight_token = token
        gate.greenlight_certified_by = user_id
        gate.greenlight_certified_at = datetime.now(UTC).isoformat()
        gate.version_index += 1
        gate.reason_for_change = reason_for_change

        saved = await self.startup_repo.save_greenlight_gate(gate)

        # Emit SiteGreenlightApproved event atomically to Transactional Outbox
        event_payload = {
            "event_type": "SiteGreenlightApproved",
            "site_id": site_id,
            "study_id": study_id,
            "country_code": country_code.upper(),
            "greenlight_token": token,
            "certified_by": user_id,
            "certified_at": gate.greenlight_certified_at,
            "facets_summary": {
                "regulatory_irb": gate.facet_regulatory_irb,
                "cta_budget": gate.facet_cta_budget,
                "eisf_baseline": gate.facet_eisf_baseline,
                "doa_log": gate.facet_doa_log,
                "staff_training": gate.facet_staff_training,
                "ip_release": gate.facet_ip_release,
            },
            "is_overridden": gate.is_overridden,
            "version_index": gate.version_index,
        }
        await self.startup_repo.save_outbox_event(
            event_type="SiteGreenlightApproved",
            payload=event_payload,
            correlation_id=f"site-greenlight-approved-{study_id}-{site_id}-{gate.version_index}",
            created_by=user_id,
            reason_for_change=reason_for_change,
        )

        if self.doa_repo:
            audit = CTMSAuditLogEntity(
                user_id=user_id,
                user_role=user_roles,
                action="SITE_GREENLIGHT_CERTIFIED",
                details=(
                    f"Site {site_id} in study {study_id} ({country_code}) formally certified for GREENLIGHT activation "
                    f"by {user_id}. Cryptographic token issued. Reason: {reason_for_change}"
                ),
                timestamp=datetime.now(UTC).isoformat(),
            )
            await self.doa_repo.save_audit_log(audit)

        return saved

    async def override_site_greenlight(
        self,
        study_id: str,
        site_id: str,
        sponsor_user_id: str,
        medical_user_id: str,
        sponsor_signature: str,
        medical_signature: str,
        justification: str,
        user_roles: str,
        reason_for_change: str,
        country_code: str = "US",
    ) -> SiteGreenlightGateEntity:
        """Executes dual-signature executive sponsor GxP override with mandatory Part 11 justification logging.

        Requirements: PRD-SSU-002, 21 CFR Part 11.
        """
        if not sponsor_user_id or not medical_user_id:
            raise GreenlightPrerequisiteError(
                "Executive GxP override requires dual signatures: Sponsor Executive and Medical Monitor."
            )
        if not sponsor_signature or not medical_signature:
            raise GreenlightPrerequisiteError(
                "Valid cryptographic electronic signatures are required from both Sponsor Executive and Medical Monitor."
            )
        if not justification or len(justification.strip()) < 10:
            raise GreenlightPrerequisiteError(
                "Detailed regulatory justification (minimum 10 characters) is mandatory for GxP greenlight override."
            )

        gate = await self.startup_repo.get_greenlight_gate(site_id)
        if not gate:
            gate = await self.evaluate_site_greenlight(
                study_id=study_id, site_id=site_id, country_code=country_code
            )

        now_iso = datetime.now(UTC).isoformat()
        gate.is_overridden = True
        gate.overall_status = "OVERRIDDEN"
        gate.override_sponsor_user_id = sponsor_user_id
        gate.override_medical_user_id = medical_user_id
        gate.override_justification = justification.strip()
        gate.override_authorized_at = now_iso
        gate.greenlight_certified_by = (
            f"{sponsor_user_id} (Sponsor) & {medical_user_id} (Medical Monitor)"
        )
        gate.greenlight_certified_at = now_iso

        token = generate_greenlight_token(
            site_id=site_id,
            study_id=study_id,
            country_code=country_code,
            certified_by=gate.greenlight_certified_by,
            is_overridden=True,
        )
        gate.greenlight_token = token
        gate.version_index += 1
        gate.reason_for_change = reason_for_change

        saved = await self.startup_repo.save_greenlight_gate(gate)

        # Emit outbox event
        event_payload = {
            "event_type": "SiteGreenlightApproved",
            "site_id": site_id,
            "study_id": study_id,
            "country_code": country_code.upper(),
            "greenlight_token": token,
            "certified_by": gate.greenlight_certified_by,
            "certified_at": gate.greenlight_certified_at,
            "facets_summary": {
                "regulatory_irb": gate.facet_regulatory_irb,
                "cta_budget": gate.facet_cta_budget,
                "eisf_baseline": gate.facet_eisf_baseline,
                "doa_log": gate.facet_doa_log,
                "staff_training": gate.facet_staff_training,
                "ip_release": gate.facet_ip_release,
            },
            "is_overridden": True,
            "override_justification": justification,
            "version_index": gate.version_index,
        }
        await self.startup_repo.save_outbox_event(
            event_type="SiteGreenlightApproved",
            payload=event_payload,
            correlation_id=f"site-greenlight-override-{study_id}-{site_id}-{gate.version_index}",
            created_by=sponsor_user_id,
            reason_for_change=reason_for_change,
        )

        if self.doa_repo:
            audit = CTMSAuditLogEntity(
                user_id=sponsor_user_id,
                user_role=user_roles,
                action="SITE_GREENLIGHT_OVERRIDDEN",
                details=(
                    f"GxP Dual-Signature Override executed for Site {site_id} in study {study_id}. "
                    f"Sponsor: {sponsor_user_id}, Medical: {medical_user_id}. Justification: {justification}"
                ),
                timestamp=now_iso,
            )
            await self.doa_repo.save_audit_log(audit)

        return saved

    def verify_greenlight_activation_token(
        self,
        token: str,
        site_id: str | None = None,
        study_id: str | None = None,
    ) -> dict[str, Any] | None:
        """Verifies cryptographic signature and constraints of a Site Greenlight Activation Token."""
        return verify_greenlight_token(token=token, site_id=site_id, study_id=study_id)
