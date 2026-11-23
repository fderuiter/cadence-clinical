import { debounce } from "ui";
import { DEMO_PARTICIPANTS } from "demo-fixtures";
import { evaluateAST } from "../../evaluator.js";
import { allowUnbackedAction } from "../../utils/unbackedActions.js";
import {
  writeStorageItem,
  type ClinicalField,
  type ClinicalQuery,
  type ClinicalState,
  type LedgerBlock,
  type SubjectFixture,
} from "../clinicalScope";

export const DEFAULT_DEMO_SUBJECTS: SubjectFixture[] = DEMO_PARTICIPANTS.map(
  (participant, idx) => {
    const isPendingReconsent = idx === 1;
    const isEnrolled = idx === 2;
    const status = isEnrolled ? "ENROLLED" : "ACTIVE";
    return {
      id: participant.subjectCode,
      recordId: participant.enrollmentUuid,
      siteId: participant.siteCode,
      site_id: participant.siteCode,
      status,
      active_protocol_version: isPendingReconsent ? "1.0.0" : "2.0.0",
      consentText: isPendingReconsent
        ? "Pending ICF v2.0.0"
        : "Signed ICF v2.0.0",
      consentColor: isPendingReconsent ? "yellow" : "green",
      category: isPendingReconsent ? "pending" : "migrated",
      isGated: isPendingReconsent,
      label: isPendingReconsent
        ? `${participant.subjectCode} (${participant.siteCode} - Pending Re-Consent)`
        : `${participant.subjectCode} (${participant.siteCode} - ${status === "ENROLLED" ? "Enrolled" : "Active"} v2.0.0)`,
    };
  }
);

export const DEFAULT_FORM_VALUES: Record<
  string,
  string | number | boolean | null | undefined
> = {
  concept_code: "",
  brthdt: "1980-05-12", // deid-ignore
  sex: "F",
  vssbp: "120",
  vsdpb: "80",
  pulse: "72",
  pulse_details: "",
  weight: "70",
  height: "1.75",
  bmi_status: "Normal",
  concept_code_vs: "",
};

export function createClinicalEcrfState(
  savedFormValues: Record<
    string,
    string | number | boolean | null | undefined
  > | null = null,
  savedFormQueries: Record<string, ClinicalQuery> | null = null
): Pick<
  ClinicalState,
  "ecrfFields" | "formValues" | "fieldVisibility" | "formQueries"
> {
  return {
    ecrfFields: [
      {
        id: "concept_code",
        label: "Concept Code Lookup (NCI Thesaurus)",
        type: "concept_code",
        gridSpan: 12,
        cdash: "DM.CONCEPT_CODE",
        value: "",
      },
      {
        id: "brthdt",
        label: "Date of Birth (YYYY-MM-DD)",
        type: "text",
        gridSpan: 6,
        cdash: "DM.BRTHDT",
        value: "1980-05-12", // deid-ignore
        validation: {
          required: true,
          pattern: "^\\d{4}-\\d{2}-\\d{2}$",
          message: "Date must be in YYYY-MM-DD format",
        },
      },
      {
        id: "sex",
        label: "Sex at Birth",
        type: "radio",
        gridSpan: 6,
        options: [
          { value: "M", label: "Male" },
          { value: "F", label: "Female" },
          { value: "U", label: "Unknown" },
        ],
        cdash: "DM.SEX",
        value: "F",
      },
      {
        id: "vssbp",
        label: "Systolic Blood Pressure (mmHg)",
        type: "text",
        gridSpan: 4,
        cdash: "VS.VSSBP",
        value: "120",
        validation: {
          required: true,
          min: 50,
          max: 250,
          message: "Systolic Blood Pressure must be between 50 and 250 mmHg",
        },
      },
      {
        id: "vsdpb",
        label: "Diastolic Blood Pressure (mmHg)",
        type: "text",
        gridSpan: 4,
        cdash: "VS.VSDPB",
        value: "80",
        validation: {
          required: true,
          min: 30,
          max: 150,
          message: "Diastolic Blood Pressure must be between 30 and 150 mmHg",
        },
      },
      {
        id: "pulse",
        label: "Pulse Rate (bpm)",
        type: "text",
        gridSpan: 4,
        cdash: "VS.VSHR",
        value: "72",
        validation: {
          required: true,
          min: 30,
          max: 200,
          message: "Pulse Rate must be between 30 and 200 bpm",
        },
      },
      {
        id: "pulse_details",
        label: "Pulse Details (Tachycardia comment)",
        type: "text",
        gridSpan: 12,
        cdash: "VS.VSHR_DETAILS",
        value: "",
        relevant: {
          type: "comparison",
          operator: ">",
          operands: [
            { type: "field_ref", field_ref: { field_id: "pulse" } },
            { type: "constant", value: 100 },
          ],
        },
      },
      {
        id: "weight",
        label: "Weight (kg)",
        type: "text",
        gridSpan: 6,
        cdash: "VS.WT",
        value: "70",
        validation: {
          required: true,
          min: 10,
          max: 300,
        },
      },
      {
        id: "height",
        label: "Height (m)",
        type: "text",
        gridSpan: 6,
        cdash: "VS.HT",
        value: "1.75",
        validation: {
          required: true,
          min: 0.5,
          max: 3.0,
        },
        constraint: {
          condition: {
            type: "comparison",
            operator: ">",
            operands: [
              { type: "field_ref", field_ref: { field_id: "height" } },
              { type: "constant", value: 0 },
            ],
          },
          query_message: "Height must be strictly greater than zero.",
        },
      },
      {
        id: "bmi_status",
        label: "BMI Status Information",
        type: "text",
        gridSpan: 12,
        cdash: "VS.BMI_STATUS",
        value: "Normal",
        relevant: {
          type: "comparison",
          operator: ">",
          operands: [
            { type: "field_ref", field_ref: { field_id: "height" } },
            { type: "constant", value: 0 },
          ],
        },
      },
      {
        id: "concept_code_vs", // Resolved duplicate ID by renaming the Vital Signs domain concept code field
        label: "NCI Thesaurus Concept Code",
        type: "concept_code",
        gridSpan: 12,
        cdash: "VS.CONCEPT_CODE",
        value: "",
        validation: {
          required: true,
        },
      },
    ],
    formValues: savedFormValues || { ...DEFAULT_FORM_VALUES },
    fieldVisibility: {},
    formQueries: savedFormQueries || {},
  };
}

type EcrfStoreContext = ClinicalState & {
  addLedgerBlock: (
    action: string,
    details: Record<string, unknown>,
    reason?: string
  ) => Promise<LedgerBlock>;
  evaluateRules: () => Promise<void>;
};

export function createClinicalEcrfActions() {
  return {
    async evaluateRules(this: EcrfStoreContext) {
      let changed = true;
      let passes = 0;
      while (changed && passes < 10) {
        changed = false;
        passes++;
        for (const field of this.ecrfFields) {
          const verdict = field.relevant
            ? evaluateAST(
                field.relevant as Record<string, unknown>,
                this.formValues as Record<string, unknown>
              )
            : true;
          const isRelevant = typeof verdict !== "boolean" || verdict;
          const wasVisible = this.fieldVisibility[field.id] ?? true;
          if (
            isRelevant !== wasVisible ||
            this.fieldVisibility[field.id] === undefined
          ) {
            this.fieldVisibility[field.id] = isRelevant;
            changed = true;
          }
          if (!isRelevant) {
            const val = this.formValues[field.id];
            if (val !== undefined && val !== "" && val !== null) {
              this.formValues[field.id] = "";
              await this.addLedgerBlock(
                "FIELD_PURGE",
                {
                  fieldId: field.id,
                  label: field.label,
                  oldValue: val,
                  newValue: "",
                },
                "System-initiated purge of inactive child variable due to parent value mutation"
              );
              changed = true;
            }
          }
        }
      }
    },

    triggerValueChange(this: EcrfStoreContext) {
      if (!this.debouncedEvaluateRules) {
        this.debouncedEvaluateRules = debounce(async () => {
          await this.evaluateRules();
        }, 50) as unknown as () => void;
      }
      this.debouncedEvaluateRules?.();
    },

    getEcrfFieldsForVersion(
      this: ClinicalState,
      version: string = "1.0.0"
    ): ClinicalField[] {
      const baseFields = this.ecrfFields;
      if (version.startsWith("2.") || version === "2.0.0") {
        const v2Fields: ClinicalField[] = [
          {
            id: "vs_temp",
            label: "Body Temperature (°C)",
            type: "text",
            gridSpan: 6,
            cdash: "VS.TEMP",
            value: "36.8",
            validation: {
              required: false,
              min: 30,
              max: 45,
              message: "Temperature must be between 30 and 45 °C",
            },
          },
          {
            id: "lb_wbc",
            label: "White Blood Cell Count (10^9/L)",
            type: "text",
            gridSpan: 6,
            cdash: "LB.WBC",
            value: "6.5",
            validation: {
              required: false,
              min: 1.0,
              max: 50.0,
              message: "WBC reference range is 4.0 - 11.0 10^9/L",
            },
          },
          {
            id: "lb_gluc",
            label: "Fasting Blood Glucose (mg/dL)",
            type: "text",
            gridSpan: 6,
            cdash: "LB.GLUC",
            value: "95",
            validation: {
              required: false,
              min: 40,
              max: 400,
              message: "Fasting Glucose reference range is 70 - 99 mg/dL",
            },
          },
          {
            id: "lb_alt",
            label: "Alanine Aminotransferase - ALT (U/L)",
            type: "text",
            gridSpan: 6,
            cdash: "LB.ALT",
            value: "25",
            validation: {
              required: false,
              min: 1,
              max: 500,
              message: "ALT reference range is 7 - 56 U/L",
            },
          },
        ];
        const combined = [...baseFields];
        for (const vf of v2Fields) {
          if (!combined.some((f) => f.id === vf.id)) {
            combined.push(vf);
          }
        }
        return combined;
      }
      return baseFields;
    },

    async enrollSubject(
      this: EcrfStoreContext,
      enrollData: {
        id: string;
        siteId?: string;
        consentDate?: string;
        armId?: string;
        protocolVersion?: string;
        reason?: string;
      }
    ) {
      const subjectId = enrollData.id.trim();
      if (!subjectId) return null;
      // #4827: enrolment is not recorded by any service from here yet; outside demo builds nothing changes.
      if (!allowUnbackedAction("Enrolling a subject")) return null;

      let sub = this.subjects.find((s) => s.id === subjectId);
      const version = enrollData.protocolVersion || "1.0.0";
      const siteId = enrollData.siteId || "SITE-101";
      const armId = enrollData.armId || "ARM-A";
      const consentDate =
        enrollData.consentDate || new Date().toISOString().split("T")[0];

      if (!sub) {
        sub = {
          id: subjectId,
          siteId: siteId,
          consentDate: consentDate,
          armId: armId,
          status: "ENROLLED",
          active_protocol_version: version,
          consentText: `Signed ICF v${version}`,
          consentColor: "green",
          category: version.startsWith("2.") ? "migrated" : "pending",
          isGated: false,
          label: `${subjectId} (Enrolled - ${armId})`,
        };
        this.subjects.push(sub);
      } else {
        sub.siteId = siteId;
        sub.consentDate = consentDate;
        sub.armId = armId;
        sub.status = "ENROLLED";
        sub.active_protocol_version = version;
        sub.isGated = false;
        sub.label = `${subjectId} (Enrolled - ${armId})`;
      }

      const customReason =
        enrollData.reason ||
        `Subject ${subjectId} enrolled at ${siteId} assigned to arm ${armId} under protocol v${version}.`;

      await this.addLedgerBlock(
        "SUBJECT_ENROLL",
        {
          subject_id: subjectId,
          site_id: siteId,
          arm_id: armId,
          consent_date: consentDate,
          protocol_version: version,
        },
        customReason
      );

      if (typeof window !== "undefined" && window.localStorage) {
        writeStorageItem("subjects", this.subjects);
      }

      return sub;
    },
  };
}
