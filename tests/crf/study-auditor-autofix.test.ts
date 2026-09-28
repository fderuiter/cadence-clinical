import { describe, it, expect } from "vitest";
import {
  StudyAuditor,
  StudyAuditReport,
  DiagnosticCategory,
  StudyProtocol,
} from "../../lib/crf/study-auditor.js";

const UUID_REGEX =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

describe("StudyAuditor AutoFix Engine [CRF-02]", () => {
  const auditor = new StudyAuditor();

  describe("Category 1: Overlength Variable Names", () => {
    it("should audit and autoFix individual overlength variable names", () => {
      const study: StudyProtocol = {
        id: "STUDY-001",
        protocolTitle: "Hypertension Study",
        forms: [
          {
            id: "form-vs",
            name: "Vital Signs",
            cdashDomain: "VS",
            items: [
              {
                id: "field-1",
                name: "VS_SYSTOLIC_BLOOD_PRESSURE",
                cdashVariable: "VS_SYSTOLIC_BLOOD_PRESSURE",
                label: "Systolic Blood Pressure",
                dataType: "integer",
                mandatory: true,
              },
            ],
          },
        ],
        encounters: [
          {
            id: "enc-1",
            name: "Visit 1",
            encounterType: "Visit",
            targetDay: 1,
            startDate: "2026-08-21",
          },
        ],
        activities: [
          {
            id: "act-1",
            name: "Vital Signs Assessment",
            cdashDomain: "VS",
            assignedVisitNames: ["Visit 1"],
          },
        ],
      };

      const report = auditor.audit(study);
      expect(report.isCompliant).toBe(false);

      const overlengthDiags = report.diagnostics.filter(
        (d) => d.category === DiagnosticCategory.OVERLENGTH_VARIABLE_NAME
      );
      expect(overlengthDiags.length).toBeGreaterThanOrEqual(1);

      const targetDiag = overlengthDiags[0];
      expect(targetDiag.autoFixable).toBe(true);

      const fixSuccess = report.autoFix(targetDiag.id);
      expect(fixSuccess).toBe(true);

      // Verify the field variable name is now <= 8 characters
      const fixedField = study.forms[0].items[0];
      expect(fixedField.cdashVariable!.length).toBeLessThanOrEqual(8);
      expect(fixedField.name.length).toBeLessThanOrEqual(8);
      expect(fixedField.cdashVariable).toMatch(/^[A-Z0-9_]{1,8}$/);
    });
  });

  describe("Category 2: Unassigned Choice Codelists", () => {
    it("should audit and autoFix unassigned choice codelists", () => {
      const study: StudyProtocol = {
        id: "STUDY-002",
        protocolTitle: "Choice Test Study",
        forms: [
          {
            id: "form-ae",
            name: "Adverse Events",
            cdashDomain: "AE",
            items: [
              {
                id: "field-serious",
                name: "AESER",
                cdashVariable: "AESER",
                label: "Serious Event?",
                dataType: "choice",
                options: [], // empty options
              },
            ],
          },
        ],
        encounters: [
          {
            id: "enc-1",
            name: "Screening",
            targetDay: 1,
            startDate: "2026-08-21",
          },
        ],
        activities: [
          {
            id: "act-ae",
            name: "Adverse Events Log",
            cdashDomain: "AE",
            assignedVisitNames: ["Screening"],
          },
        ],
      };

      const report = auditor.audit(study);
      expect(report.isCompliant).toBe(false);

      const choiceDiags = report.diagnostics.filter(
        (d) => d.category === DiagnosticCategory.UNASSIGNED_CHOICE_CODELIST
      );
      expect(choiceDiags.length).toBe(1);

      const targetDiag = choiceDiags[0];
      const fixSuccess = report.autoFix(targetDiag.id);
      expect(fixSuccess).toBe(true);

      // Verify options and codelist populated with standard choices
      const fixedField = study.forms[0].items[0];
      expect(fixedField.options).toBeDefined();
      expect(fixedField.options!.length).toBeGreaterThan(0);
      expect(fixedField.options).toEqual(["Yes", "No", "Unknown"]);
    });
  });

  describe("Category 3: Non-ISO Dates", () => {
    it("should audit and autoFix non-ISO formatted date strings", () => {
      const study: StudyProtocol = {
        id: "STUDY-003",
        protocolTitle: "Date Formatting Study",
        encounters: [
          {
            id: "enc-1",
            name: "Baseline",
            targetDay: 1,
            startDate: "08/21/2026", // Non-ISO format
            endDate: "2026/08/25", // Non-ISO format
          },
        ],
        forms: [
          {
            id: "form-demo",
            name: "Demographics",
            cdashDomain: "DM",
            items: [
              {
                id: "field-brthdtc",
                name: "BRTHDTC",
                cdashVariable: "BRTHDTC",
                label: "Date of Birth",
                dataType: "date",
                defaultValue: "21-08-1990", // Non-ISO date
              },
            ],
          },
        ],
        activities: [
          {
            id: "act-demo",
            name: "Demographics",
            cdashDomain: "DM",
            assignedVisitNames: ["Baseline"],
          },
        ],
      };

      const report = auditor.audit(study);
      expect(report.isCompliant).toBe(false);

      const dateDiags = report.diagnostics.filter(
        (d) => d.category === DiagnosticCategory.NON_ISO_DATE
      );
      expect(dateDiags.length).toBeGreaterThanOrEqual(2);

      for (const diag of dateDiags) {
        expect(report.autoFix(diag.id)).toBe(true);
      }

      // Verify all dates converted to ISO YYYY-MM-DD
      expect(study.encounters[0].startDate).toBe("2026-08-21");
      expect(study.encounters[0].endDate).toBe("2026-08-25");
      expect(study.forms[0].items[0].defaultValue).toBe("1990-08-21");
    });
  });

  describe("Category 4: Missing CDASH Core Variables", () => {
    it("should audit and autoFix missing CDASH core domain variables with cryptographic UUIDs", () => {
      const study: StudyProtocol = {
        id: "STUDY-004",
        protocolTitle: "CDASH Core Compliance Study",
        forms: [
          {
            id: "form-dm",
            name: "Demographics",
            cdashDomain: "DM",
            items: [
              {
                id: "field-custom",
                name: "ETHNIC",
                cdashVariable: "ETHNIC",
                label: "Ethnicity",
                dataType: "text",
              },
              // Missing DM core variables: BRTHDTC, AGE, SEX, ARM
            ],
          },
          {
            id: "form-vs",
            name: "Vital Signs",
            cdashDomain: "VS",
            items: [
              // Missing VS core variables: VSTESTCD, VSORRES, VSDTC
            ],
          },
        ],
        encounters: [
          {
            id: "enc-1",
            name: "Visit 1",
            targetDay: 1,
            startDate: "2026-08-21",
          },
        ],
        activities: [
          {
            id: "act-dm",
            name: "Demographics",
            cdashDomain: "DM",
            assignedVisitNames: ["Visit 1"],
          },
          {
            id: "act-vs",
            name: "Vital Signs",
            cdashDomain: "VS",
            assignedVisitNames: ["Visit 1"],
          },
        ],
      };

      const report = auditor.audit(study);
      expect(report.isCompliant).toBe(false);

      const coreDiags = report.diagnostics.filter(
        (d) => d.category === DiagnosticCategory.MISSING_CDASH_CORE_VARIABLE
      );
      expect(coreDiags.length).toBeGreaterThanOrEqual(2);

      for (const diag of coreDiags) {
        expect(report.autoFix(diag.id)).toBe(true);
      }

      // Check DM form items
      const dmVariables = study.forms[0].items.map((i) => i.cdashVariable);
      expect(dmVariables).toContain("BRTHDTC");
      expect(dmVariables).toContain("AGE");
      expect(dmVariables).toContain("SEX");
      expect(dmVariables).toContain("ARM");

      // Check VS form items
      const vsVariables = study.forms[1].items.map((i) => i.cdashVariable);
      expect(vsVariables).toContain("VSTESTCD");
      expect(vsVariables).toContain("VSORRES");
      expect(vsVariables).toContain("VSDTC");

      // Verify all newly generated field IDs are cryptographically safe UUIDs
      const newItems = [
        ...study.forms[0].items.filter((i) => i.cdashVariable !== "ETHNIC"),
        ...study.forms[1].items,
      ];
      for (const item of newItems) {
        expect(item.id).toMatch(UUID_REGEX);
      }
    });
  });

  describe("Category 5: Orphaned SoA Forms", () => {
    it("should audit and autoFix orphaned SoA forms by linking to encounters", () => {
      const study: StudyProtocol = {
        id: "STUDY-005",
        protocolTitle: "SoA Alignment Study",
        encounters: [
          {
            id: "enc-screening",
            name: "Screening",
            targetDay: -7,
            startDate: "2026-08-14",
          },
          {
            id: "enc-baseline",
            name: "Day 1 (Baseline)",
            targetDay: 1,
            startDate: "2026-08-21",
          },
        ],
        forms: [
          {
            id: "form-ecg",
            name: "12-Lead ECG",
            cdashDomain: "EG",
            items: [
              {
                id: "eg-1",
                name: "EGTESTCD",
                cdashVariable: "EGTESTCD",
                label: "ECG Test Code",
                dataType: "text",
              },
            ],
          },
        ],
        activities: [
          {
            id: "act-ecg",
            name: "12-Lead ECG Assessment",
            cdashDomain: "EG",
            assignedVisitNames: [], // Orphaned!
            assignedEncounterIds: [],
          },
        ],
      };

      const report = auditor.audit(study);
      expect(report.isCompliant).toBe(false);

      const orphanedDiags = report.diagnostics.filter(
        (d) => d.category === DiagnosticCategory.ORPHANED_SOA_FORM
      );
      expect(orphanedDiags.length).toBeGreaterThanOrEqual(1);

      const targetDiag = orphanedDiags[0];
      expect(report.autoFix(targetDiag.id)).toBe(true);

      // Verify that activity is now assigned to the baseline visit
      const activity = study.activities[0];
      expect(activity.assignedVisitNames.length).toBeGreaterThan(0);
      expect(activity.assignedVisitNames).toContain("Day 1 (Baseline)");
    });

    it("should create a default encounter with crypto UUID if study has no encounters", () => {
      const study: StudyProtocol = {
        id: "STUDY-005B",
        protocolTitle: "No Encounter Study",
        encounters: [],
        forms: [
          {
            id: "form-lab",
            name: "Lab",
            cdashDomain: "LB",
            items: [
              {
                id: "lb-1",
                name: "LBTESTCD",
                cdashVariable: "LBTESTCD",
                label: "Lab Test",
                dataType: "text",
              },
            ],
          },
        ],
        activities: [
          {
            id: "act-lab",
            name: "Lab Chemistry",
            cdashDomain: "LB",
            assignedVisitNames: [],
          },
        ],
      };

      const report = auditor.audit(study);
      const orphanedDiags = report.diagnostics.filter(
        (d) => d.category === DiagnosticCategory.ORPHANED_SOA_FORM
      );
      expect(orphanedDiags.length).toBeGreaterThanOrEqual(1);

      expect(report.autoFix(orphanedDiags[0].id)).toBe(true);
      expect(study.encounters.length).toBe(1);
      expect(study.encounters[0].id).toMatch(UUID_REGEX);
      expect(study.activities[0].assignedVisitNames).toContain(
        study.encounters[0].name
      );
    });
  });

  describe("Batch autoFixAll() & Subsequent Compliance Pass", () => {
    it("should remediate all 5 categories in batch and yield isCompliant === true on re-audit", () => {
      const invalidStudy: StudyProtocol = {
        id: "STUDY-BATCH-001",
        protocolTitle: "Multi-Defect Clinical Trial",
        encounters: [
          {
            id: "enc-1",
            name: "Baseline Visit",
            targetDay: 1,
            startDate: "08/21/2026", // 3. Non-ISO date
          },
        ],
        forms: [
          {
            id: "form-vs",
            name: "Vital Signs",
            cdashDomain: "VS",
            items: [
              {
                id: "f-1",
                name: "VS_EXTREMELY_LONG_VARIABLE_NAME", // 1. Overlength variable name (>8 chars)
                cdashVariable: "VS_EXTREMELY_LONG_VARIABLE_NAME",
                label: "Extremely Long Variable",
                dataType: "choice", // 2. Unassigned choice codelist
                options: [],
              },
              // 4. Missing CDASH core variables (VSTESTCD, VSORRES, VSDTC)
            ],
          },
          {
            id: "form-dm",
            name: "Demographics",
            cdashDomain: "DM",
            items: [
              {
                id: "f-dm-1",
                name: "DM_DATE_OF_BIRTH", // 1. Overlength
                cdashVariable: "DM_DATE_OF_BIRTH",
                label: "Date of Birth",
                dataType: "date",
                defaultValue: "21/08/1985", // 3. Non-ISO date
              },
              // 4. Missing CDASH core variables (BRTHDTC, AGE, SEX, ARM)
            ],
          },
        ],
        activities: [
          {
            id: "act-vs",
            name: "Vital Signs Assessment",
            cdashDomain: "VS",
            assignedVisitNames: ["Baseline Visit"],
          },
          {
            id: "act-dm",
            name: "Demographics Collection",
            cdashDomain: "DM",
            assignedVisitNames: [], // 5. Orphaned SoA form
          },
        ],
      };

      // Initial audit
      const initialReport = auditor.audit(invalidStudy);
      expect(initialReport.isCompliant).toBe(false);
      expect(initialReport.diagnostics.length).toBeGreaterThan(0);

      // Verify all 5 categories are detected
      const categoriesFound = new Set(
        initialReport.diagnostics.map((d) => d.category)
      );
      expect(categoriesFound.has(DiagnosticCategory.OVERLENGTH_VARIABLE_NAME)).toBe(true);
      expect(categoriesFound.has(DiagnosticCategory.UNASSIGNED_CHOICE_CODELIST)).toBe(true);
      expect(categoriesFound.has(DiagnosticCategory.NON_ISO_DATE)).toBe(true);
      expect(categoriesFound.has(DiagnosticCategory.MISSING_CDASH_CORE_VARIABLE)).toBe(true);
      expect(categoriesFound.has(DiagnosticCategory.ORPHANED_SOA_FORM)).toBe(true);

      // Execute 1-Click AutoFix All
      const batchResult = initialReport.autoFixAll();
      expect(batchResult.fixedCount).toBeGreaterThanOrEqual(5);
      expect(batchResult.errors.length).toBe(0);

      // Subsequent audit pass MUST yield isCompliant === true
      const secondPassReport = auditor.audit(invalidStudy);
      expect(secondPassReport.isCompliant).toBe(true);
      expect(secondPassReport.diagnostics.filter((d) => d.severity === "error")).toHaveLength(0);

      // Verify cryptographic UUID uniqueness for generated items
      const generatedIds = invalidStudy.forms
        .flatMap((f) => f.items)
        .map((i) => i.id);
      const uniqueIds = new Set(generatedIds);
      expect(uniqueIds.size).toBe(generatedIds.length);
    });
  });
});
