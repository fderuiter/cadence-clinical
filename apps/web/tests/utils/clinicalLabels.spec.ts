import { describe, expect, it } from "vitest";
import {
  extractAiProvenanceAttributes,
  formatClinicalEnum,
  formatClinicalStatus,
  formatDocumentModality,
  formatRoleLabel,
  formatRoleList,
} from "../../src/utils/clinicalLabels";

describe("clinicalLabels", () => {
  it("maps RBAC role slugs and aliases to plain clinical role labels aligned with RoleNormalization.cs", () => {
    expect(formatRoleLabel("sysadmin")).toBe("System Administrator");
    expect(formatRoleLabel("sponsor_admin")).toBe("Sponsor Administrator");
    expect(formatRoleLabel("sponsor_designer")).toBe("Protocol Designer");
    expect(formatRoleLabel("sponsor_dm")).toBe("Data Manager");
    expect(formatRoleLabel("sponsor_mm")).toBe("Medical Monitor");
    expect(formatRoleLabel("safety_reviewer")).toBe(
      "Pharmacovigilance & Safety"
    );
    expect(formatRoleLabel("sponsor_statistician")).toBe("Biostatistician");
    expect(formatRoleLabel("unblinded_statistician")).toBe(
      "Unblinded Statistician"
    );
    expect(formatRoleLabel("principal_investigator")).toBe(
      "Principal Investigator"
    );
    expect(formatRoleLabel("site_investigator")).toBe("Site Investigator");
    expect(formatRoleLabel("sub_investigator")).toBe("Sub-Investigator");
    expect(formatRoleLabel("site_crc")).toBe("Clinical Research Coordinator");
    expect(formatRoleLabel("cra_monitor")).toBe("Clinical Research Associate");
    expect(formatRoleLabel("pharmacist")).toBe("Site Pharmacist");
    expect(formatRoleLabel("tmf_auditor")).toBe("eTMF Auditor");
  });

  it("preserves already human-formatted role phrases and deduplicates role lists", () => {
    expect(formatRoleLabel("Principal Investigator")).toBe(
      "Principal Investigator"
    );
    expect(
      formatRoleList(["sponsor_dm", "Data Manager", "sponsor_designer"])
    ).toBe("Data Manager, Protocol Designer");
    expect(formatRoleList([], "Unassigned")).toBe("Unassigned");
  });

  it("formats workflow statuses, operational enums, and document modalities while preserving regulatory acronyms", () => {
    expect(formatClinicalStatus("TECHNICAL_QC")).toBe("Technical QC");
    expect(formatClinicalStatus("PENDING_PI_SIGNATURE")).toBe(
      "Pending PI Signature"
    );
    expect(formatClinicalStatus("SIGNED_OFF")).toBe("Signed Off");
    expect(formatClinicalStatus("IN_PROGRESS")).toBe("In Progress");
    expect(formatClinicalEnum("FIRST_SUBJECT_ENROLLED")).toBe(
      "First Participant Enrolled"
    );
    expect(formatClinicalEnum("DB_LOCK")).toBe("Database Lock");
    expect(formatClinicalEnum("SIV")).toBe("Site Initiation (SIV)");
    expect(formatClinicalEnum("follow_up_letter")).toBe("Follow-Up Letter");
    expect(formatDocumentModality("PDF_BINARY")).toBe("PDF Document");
    expect(formatDocumentModality("STRUCTURED_FORM")).toBe("Structured Form");
  });

  it("extracts readable provenance attributes from AI generation manifests without raw JSON syntax", () => {
    const rows = extractAiProvenanceAttributes({
      model_version: "cadence-tmf-v3.2",
      confidence_score: 0.96,
      qc_gate: "CRA_QC_REQUIRED",
      verified_pages: 4,
      redaction_applied: false,
    });

    expect(rows).toEqual([
      { label: "Model Version", value: "cadence-tmf-v3.2" },
      { label: "Confidence Score", value: "96%" },
      { label: "QC Gate", value: "CRA QC Required" },
      { label: "Verified Pages", value: "4" },
      { label: "Redaction Applied", value: "No" },
    ]);
    expect(extractAiProvenanceAttributes(null)).toEqual([]);
  });
});
