import { defineStore, getActivePinia, type StateTree } from "pinia";
import "pinia";
import { stateTrackingPlugin } from "./plugins.js";

declare module "pinia" {
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  export interface DefineStoreOptionsBase<S extends StateTree, Store> {
    trackActions?: Record<string, { loading?: string; error?: string }>;
  }
}
import { buildLedgerBlock, debounce } from "ui";
import { useAuthStore } from "./auth.js";
import { soaClient } from "../api/soaClient.js";
import { executionService } from "../api/execution";
import { evaluateAST } from "../evaluator.js";
import { ingestionClient } from "../api/ingestionClient.js";
import { normalizeUsdm, type NormalizedUsdm } from "./normalization";
import {
  CodeSchema,
  ActivitySchema,
  EncounterSchema,
  StudyArmSchema,
  StudyEpochSchema,
  USDMStudySchema,
  validateUsdmGraph,
} from "usdm-schemas";

export interface ClinicalFieldOption {
  value: string;
  label: string;
}

export interface ClinicalFieldValidation {
  required?: boolean;
  pattern?: string;
  message?: string;
  min?: number;
  max?: number;
}

export interface ClinicalField {
  id: string;
  label: string;
  type: string;
  gridSpan: number;
  cdash?: string;
  value?: string | number | boolean | null;
  validation?: ClinicalFieldValidation;
  relevant?: Record<string, unknown>;
  constraint?: Record<string, unknown>;
  options?: ClinicalFieldOption[];
}

export interface CtmsMilestone {
  id: string;
  type: string;
  plannedDate: string;
  actualDate: string;
  status: string;
}

export interface CtmsVisit {
  id: string;
  type: string;
  scheduledDate: string;
  actualDate: string;
  status: string;
  cra: string;
}

export interface CtmsAllocation {
  cra: string;
  activeAllocations: number;
  sites: string[];
  studies: string[];
}

export interface CtmsRecruitment {
  siteId: string;
  screened: number;
  enrolled: number;
  target: number;
}

export interface CtmsData {
  milestones: CtmsMilestone[];
  visits: CtmsVisit[];
  allocations: CtmsAllocation[];
  recruitment: CtmsRecruitment[];
}

export interface ClinicalQuery {
  id?: string;
  fieldId?: string;
  field_id?: string;
  status?: string;
  text?: string;
  query_text?: string;
  author?: string;
  created_at?: string;
  response?: string;
  response_text?: string;
  responded_by?: string;
  responded_at?: string;
  closed_by?: string;
  closed_at?: string;
}

export interface LabAlert {
  id?: string;
  study_id?: string;
  subject_id?: string;
  test_code?: string;
  test_name?: string;
  severity?: string;
  value?: string | number;
  unit?: string;
  message?: string;
  flag?: string;
}

export interface LedgerBlock {
  index: number;
  timestamp: string;
  action: string;
  details: Record<string, unknown>;
  reason: string;
  prevHash: string;
  hash: string;
  synced: boolean;
}

export interface IngestionItem {
  id: string;
  status: string;
  name?: string;
  cdash_domain?: string;
}

export interface CandidateDraft {
  id: string;
  status: string;
  items?: IngestionItem[];
  created_at?: string;
  updated_at?: string;
}

export interface IngestionJob {
  job_id: string;
  status: string;
  candidate_id: string;
  errors: string[] | null;
}

export interface SubjectFixture {
  id: string;
  siteId?: string;
  consentDate?: string;
  armId?: string;
  status: string;
  active_protocol_version: string;
  consentText: string;
  consentColor: string;
  category: string;
  isGated: boolean;
  label?: string;
}

export interface ClinicalState {
  currentUsdm: NormalizedUsdm;
  currentCtmsData: CtmsData;
  subjects: SubjectFixture[];
  ecrfFields: ClinicalField[];
  formValues: Record<string, string | number | boolean | null | undefined>;
  fieldVisibility: Record<string, boolean>;
  formQueries: Record<string, ClinicalQuery>;
  labAlerts: Record<string, LabAlert>;
  labAlertsLoading: boolean;
  labAlertsError: string | null;
  ledgerBlocks: LedgerBlock[];
  syncInterval: ReturnType<typeof setInterval> | number | null;

  activeStudyId: string;
  activeSiteId: string;
  activeSubjectId: string;
  activeVisitId: string;

  // --- SoA State ---
  activeStudyVersionId: string;
  soaLoading: boolean;
  soaError: string | null;

  // --- Ingestion / Candidate Draft State ---
  candidateDraft: CandidateDraft | null;
  ingestionJobs: IngestionJob[];
  ingestionLoading: boolean;
  ingestionError: string | null;

  debouncedEvaluateRules?: () => void;
}

const useClinicalStoreInner = defineStore("clinical", {
  state: (): ClinicalState => {
    let savedFormValues: Record<
      string,
      string | number | boolean | null | undefined
    > | null = null;
    let savedFormQueries: Record<string, ClinicalQuery> | null = null;
    let savedLedgerBlocks: LedgerBlock[] | null = null;
    let savedUsdm: unknown = null;
    let savedSubjects: SubjectFixture[] | null = null;
    if (typeof window !== "undefined" && window.localStorage) {
      try {
        const storedSubjects = window.localStorage.getItem("subjects");
        if (storedSubjects) {
          savedSubjects = JSON.parse(storedSubjects);
        }
        const storedFormValues = window.localStorage.getItem("formValues");
        if (storedFormValues) {
          savedFormValues = JSON.parse(storedFormValues);
        }
        const storedFormQueries = window.localStorage.getItem("formQueries");
        if (storedFormQueries) {
          savedFormQueries = JSON.parse(storedFormQueries);
        }
        const storedLedgerBlocks = window.localStorage.getItem("ledgerBlocks");
        if (storedLedgerBlocks) {
          savedLedgerBlocks = JSON.parse(storedLedgerBlocks);
        }
        const storedUsdm = window.localStorage.getItem("currentUsdm");
        if (storedUsdm) {
          savedUsdm = JSON.parse(storedUsdm);
        }
      } catch (e) {
        console.error("Failed to parse saved state from localStorage", e);
      }
    }

    return {
      currentUsdm: normalizeUsdm(
        savedUsdm || {
          studyId: "STUDY-USDM-001",
          studyTitle: "Phase II Trial of Cadence-001 in Essential Hypertension",
          objectives: [
            {
              id: "OBJ-001",
              type: "Primary",
              description:
                "Evaluate the reduction of mean sitting Systolic Blood Pressure (SBP) from baseline.",
            },
            {
              id: "OBJ-002",
              type: "Secondary",
              description:
                "Evaluate safety and tolerability of daily oral administration of Cadence-001.",
            },
          ],
          arms: [
            { arm_id: "ARM-A", arm_name: "Arm A: Active 10mg daily" },
            { arm_id: "ARM-B", arm_name: "Arm B: Placebo Control" },
          ],
          epochs: [
            { epoch_id: "EP-SCR", epoch_name: "Screening", sequence: 1 },
            {
              epoch_id: "EP-TRT-A",
              epoch_name: "Treatment Phase",
              sequence: 2,
              arm_id: "ARM-A",
            },
            {
              epoch_id: "EP-TRT-B",
              epoch_name: "Treatment Phase",
              sequence: 2,
              arm_id: "ARM-B",
            },
          ],
          encounters: [
            {
              encounter_id: "V-SCR",
              encounter_name: "Day -7 to -1",
              epoch_id: "EP-SCR",
              sequence: 1,
            },
            {
              encounter_id: "V-TRT-A1",
              encounter_name: "Week 2",
              epoch_id: "EP-TRT-A",
              sequence: 2,
            },
            {
              encounter_id: "V-TRT-A2",
              encounter_name: "Week 4",
              epoch_id: "EP-TRT-A",
              sequence: 3,
            },
            {
              encounter_id: "V-TRT-B1",
              encounter_name: "Week 2",
              epoch_id: "EP-TRT-B",
              sequence: 4,
            },
          ],
          rows: [
            {
              activity_id: "ACT-DEM",
              activity_name: "Informed Consent & Demographics",
              cells: [
                {
                  encounter_id: "V-SCR",
                  is_applicable: true,
                  details: "Mandatory",
                },
                { encounter_id: "V-TRT-A1", is_applicable: false },
                { encounter_id: "V-TRT-A2", is_applicable: false },
                { encounter_id: "V-TRT-B1", is_applicable: false },
              ],
            },
            {
              activity_id: "ACT-VS",
              activity_name: "Vital Signs (BP & Pulse)",
              cells: [
                {
                  encounter_id: "V-SCR",
                  is_applicable: true,
                  details: "Day -7",
                },
                {
                  encounter_id: "V-TRT-A1",
                  is_applicable: true,
                  details: "Within 10 mins",
                },
                {
                  encounter_id: "V-TRT-A2",
                  is_applicable: true,
                  details: "Conditional",
                },
                {
                  encounter_id: "V-TRT-B1",
                  is_applicable: true,
                  details: "Within 10 mins",
                },
              ],
            },
            {
              activity_id: "ACT-AE",
              activity_name: "Adverse Events Check",
              cells: [
                { encounter_id: "V-SCR", is_applicable: false },
                {
                  encounter_id: "V-TRT-A1",
                  is_applicable: true,
                  details: "Continuous",
                },
                {
                  encounter_id: "V-TRT-A2",
                  is_applicable: true,
                  details: "Continuous",
                },
                {
                  encounter_id: "V-TRT-B1",
                  is_applicable: true,
                  details: "Optional",
                },
              ],
            },
            {
              activity_id: "ACT-MED",
              activity_name: "Study Medication Log",
              cells: [
                { encounter_id: "V-SCR", is_applicable: false },
                {
                  encounter_id: "V-TRT-A1",
                  is_applicable: true,
                  details: "Daily entry",
                },
                {
                  encounter_id: "V-TRT-A2",
                  is_applicable: true,
                  details: "Daily entry",
                },
                {
                  encounter_id: "V-TRT-B1",
                  is_applicable: true,
                  details: "Daily entry",
                },
              ],
            },
          ],
        }
      ),
      currentCtmsData: {
        milestones: [
          {
            id: "M1",
            type: "SITE_SELECTION",
            plannedDate: "2026-08-01", // deid-ignore
            actualDate: "2026-08-01", // deid-ignore
            status: "ACHIEVED",
          },
          {
            id: "M2",
            type: "INITIATION_VISIT",
            plannedDate: "2026-08-10", // deid-ignore
            actualDate: "2026-08-12", // deid-ignore
            status: "ACHIEVED",
          },
          {
            id: "M3",
            type: "SITE_ACTIVATION",
            plannedDate: "2026-08-15", // deid-ignore
            actualDate: "",
            status: "PLANNED",
          },
          {
            id: "M4",
            type: "FIRST_SUBJECT_ENROLLED",
            plannedDate: "2026-09-01", // deid-ignore
            actualDate: "",
            status: "PLANNED",
          },
        ],
        visits: [
          {
            id: "V1",
            type: "SIV",
            scheduledDate: "2026-08-10", // deid-ignore
            actualDate: "2026-08-12", // deid-ignore
            status: "SIGNED_OFF",
            cra: "cra_fderuiter",
          },
          {
            id: "V2",
            type: "IMV",
            scheduledDate: "2026-08-25", // deid-ignore
            actualDate: "",
            status: "SCHEDULED",
            cra: "cra_fderuiter",
          },
        ],
        allocations: [
          {
            cra: "cra_fderuiter",
            activeAllocations: 3,
            sites: ["Site-01", "Site-02", "Site-09"],
            studies: ["STUDY-01", "STUDY-02"],
          },
          {
            cra: "cra_alice",
            activeAllocations: 1,
            sites: ["Site-03"],
            studies: ["STUDY-01"],
          },
        ],
        recruitment: [
          { siteId: "Site-01", screened: 15, enrolled: 8, target: 20 },
          { siteId: "Site-02", screened: 8, enrolled: 4, target: 15 },
        ],
      },
      subjects: savedSubjects || [
        {
          id: "SUBJ-101",
          status: "ACTIVE",
          active_protocol_version: "2.0.0",
          consentText: "Signed ICF v2.0.0",
          consentColor: "green",
          category: "migrated",
          isGated: false,
          label: "SUBJ-101 (Active - Migrated v2.0.0)",
        },
        {
          id: "SUBJ-102",
          status: "ACTIVE",
          active_protocol_version: "1.0.0",
          consentText: "Pending ICF v2.0.0",
          consentColor: "yellow",
          category: "pending",
          isGated: true,
          label: "SUBJ-102 (Active - Pending Re-Consent)",
        },
        {
          id: "SUBJ-103",
          status: "ENROLLED",
          active_protocol_version: "1.0.0",
          consentText: "Pending ICF v2.0.0",
          consentColor: "yellow",
          category: "pending",
          isGated: true,
          label: "SUBJ-103 (Enrolled - Pending Re-Consent)",
        },
        {
          id: "SUBJ-104",
          status: "COMPLETED",
          active_protocol_version: "1.0.0",
          consentText: "Historical v1.0.0",
          consentColor: "gray",
          category: "completedPrev",
          isGated: false,
          label: "SUBJ-104 (Completed - Historical v1.0.0)",
        },
        {
          id: "SUBJ-105",
          status: "ACTIVE",
          active_protocol_version: "2.0.0",
          consentText: "Signed ICF v2.0.0",
          consentColor: "green",
          category: "migrated",
          isGated: false,
          label: "SUBJ-105 (Active - Migrated v2.0.0)",
        },
        {
          id: "SUBJ-001",
          status: "ACTIVE",
          active_protocol_version: "2.0.0",
          consentText: "Signed ICF v2.0.0",
          consentColor: "green",
          category: "migrated",
          isGated: false,
          label: "SUBJ-001 (Mock Subject)",
        },
        {
          id: "SUBJ-002",
          status: "ACTIVE",
          active_protocol_version: "1.0.0",
          consentText: "Pending ICF v2.0.0",
          consentColor: "yellow",
          category: "pending",
          isGated: true,
          label: "SUBJ-002 (Screened Cohort - Pending Re-Consent)",
        },
        {
          id: "SUBJ-003",
          status: "ENROLLED",
          active_protocol_version: "2.0.0",
          consentText: "Signed ICF v2.0.0",
          consentColor: "green",
          category: "migrated",
          isGated: false,
          label: "SUBJ-003 (Post-Randomization)",
        },
      ],
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
      formValues: savedFormValues || {
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
      },
      fieldVisibility: {},
      formQueries: savedFormQueries || {},
      labAlerts: {},
      labAlertsLoading: false,
      labAlertsError: null,
      ledgerBlocks: savedLedgerBlocks || [],
      syncInterval: null,

      activeStudyId: "STUDY-USDM-001",
      activeSiteId: "SITE-001",
      activeSubjectId: "SUBJ-001",
      activeVisitId: "Screening",

      // --- SoA State ---
      activeStudyVersionId: "v_draft_01",
      soaLoading: false,
      soaError: null,

      // --- Ingestion / Candidate Draft State ---
      candidateDraft: null,
      ingestionJobs: [],
      ingestionLoading: false,
      ingestionError: null,
    };
  },
  trackActions: {
    fetchLabAlerts: { loading: "labAlertsLoading", error: "labAlertsError" },
    fetchSoAProjection: { loading: "soaLoading", error: "soaError" },
    pushSoAMutation: { loading: "soaLoading", error: "soaError" },
    pushSoALink: { loading: "soaLoading", error: "soaError" },
    uploadProtocolDocument: {
      loading: "ingestionLoading",
      error: "ingestionError",
    },
  },
  getters: {
    user: () => {
      const authStore = useAuthStore();
      if (authStore.isDemoMode && !authStore.isAuthenticated) {
        return {
          username: "fderuiter",
          roles: ["Monitor", "Sponsor Admin"],
          authenticated: true,
        };
      }
      return {
        username: authStore.identity?.username || "unknown",
        roles: authStore.normalizedRoles,
        authenticated: true,
      };
    },
    canManageQueries: () => {
      const authStore = useAuthStore();
      const roles = authStore ? authStore.normalizedRoles || [] : [];
      return roles.some((role: string) =>
        [
          "cra",
          "monitor",
          "data_manager",
          "sponsor_admin",
          "admin",
          "sponsor_designer",
        ].includes(role)
      );
    },
    getQueryLabel: () => {
      return (query: ClinicalQuery) => {
        const status =
          query && query.status ? query.status.toUpperCase() : "NONE";
        const authStore = useAuthStore();
        if (!authStore || !authStore.isAuthenticated) {
          return status;
        }
        const roles = authStore.normalizedRoles || [];
        if (roles.length === 0) {
          return status;
        }
        const isSite =
          roles.includes("site_investigator") || roles.includes("crc");
        const isMonitor = roles.includes("cra") || roles.includes("monitor");

        if (status === "OPEN" || status === "REOPENED") {
          if (isSite) return "Awaiting Site Action";
          if (isMonitor) return "Awaiting Site Response";
        } else if (status === "ANSWERED") {
          if (isSite) return "Submitted to CRA";
          if (isMonitor) return "Awaiting CRA Review";
        }
        return status;
      };
    },
    gatedSubjectIds: (state) => {
      return state.subjects.filter((s) => s.isGated).map((s) => s.id);
    },
    subjectIds: (state) => {
      return state.subjects.map((s) => s.id);
    },
    isSubjectGated: (state) => (subjectId: string) => {
      const sub = state.subjects.find((s) => s.id === subjectId);
      return sub ? sub.isGated : false;
    },
  },
  actions: {
    async evaluateRules() {
      let changed = true;
      let passes = 0;
      while (changed && passes < 10) {
        changed = false;
        passes++;
        for (const field of this.ecrfFields) {
          const isRelevant = field.relevant
            ? evaluateAST(
                field.relevant as Record<string, unknown>,
                this.formValues as Record<string, unknown>
              ) !== false
            : true;
          const wasVisible = this.fieldVisibility[field.id] !== false;
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
    triggerValueChange() {
      if (!this.debouncedEvaluateRules) {
        this.debouncedEvaluateRules = debounce(async () => {
          await this.evaluateRules();
        }, 50) as unknown as () => void;
      }
      this.debouncedEvaluateRules?.();
    },
    async addLedgerBlock(
      action: string,
      details: Record<string, unknown>,
      reason: string = "System Action"
    ) {
      const timestamp = new Date().toISOString();
      const index = this.ledgerBlocks.length;
      const prevHash =
        index === 0
          ? "0000000000000000000000000000000000000000000000000000000000000000" // deid-ignore
          : this.ledgerBlocks[index - 1].hash;

      const block = (await buildLedgerBlock(
        index,
        timestamp,
        action,
        details,
        reason,
        prevHash
      )) as unknown as LedgerBlock;
      block.synced = false;

      // Validate USDM Graph before persisting state to localStorage!
      const graphValidation = validateUsdmGraph(this.currentUsdm, {
        fields: this.ecrfFields,
      });
      if (!graphValidation.valid) {
        const cycleError = graphValidation.errors.find(
          (e) => e.code === "CYCLE_DETECTED"
        );
        const errorMsg = graphValidation.errors
          .map((e) => e.message)
          .join("; ");
        if (
          cycleError &&
          typeof window !== "undefined" &&
          typeof window.alert === "function"
        ) {
          window.alert(`Skip-Logic Cycle Alert: ${cycleError.message}`);
        }
        console.error(
          `Local storage persistence blocked by USDM Graph Validator: ${errorMsg}`
        );
        this.soaError = errorMsg;
        throw new Error(
          `Local storage persistence blocked by USDM Graph Validator: ${errorMsg}`
        );
      }

      this.ledgerBlocks.push(block);

      // Save persistent fields to localStorage
      if (typeof window !== "undefined" && window.localStorage) {
        window.localStorage.setItem("subjects", JSON.stringify(this.subjects));
        window.localStorage.setItem(
          "formValues",
          JSON.stringify(this.formValues)
        );
        window.localStorage.setItem(
          "formQueries",
          JSON.stringify(this.formQueries)
        );
        window.localStorage.setItem(
          "ledgerBlocks",
          JSON.stringify(this.ledgerBlocks)
        );
        window.localStorage.setItem(
          "currentUsdm",
          JSON.stringify(this.currentUsdm)
        );
      }

      return block;
    },
    async clearReconsentGate(
      subjectId: string,
      method: string = "ECONSENT",
      reason?: string
    ) {
      let sub = this.subjects.find((s) => s.id === subjectId);
      if (!sub) {
        sub = {
          id: subjectId,
          status: "ACTIVE",
          active_protocol_version: "2.0.0",
          consentText: "Signed ICF v2.0.0",
          consentColor: "green",
          category: "migrated",
          isGated: false,
          label: `${subjectId} (Re-Consented)`,
        };
        this.subjects.push(sub);
      } else {
        sub.isGated = false;
        sub.active_protocol_version = "2.0.0";
        sub.consentText = "Signed ICF v2.0.0";
        sub.consentColor = "green";
        sub.category = "migrated";
      }

      const customReason =
        reason ||
        `Subject ${subjectId} re-consent recorded via ${method}. Gating unlocked.`;

      await this.addLedgerBlock(
        "RECONSENT_COMPLETED",
        {
          subject_id: subjectId,
          protocol_version: "2.0.0",
          method: method,
        },
        customReason
      );

      if (typeof window !== "undefined" && window.localStorage) {
        window.localStorage.setItem("subjects", JSON.stringify(this.subjects));
      }
    },
    getEcrfFieldsForVersion(version: string = "1.0.0"): ClinicalField[] {
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
    async enrollSubject(enrollData: {
      id: string;
      siteId?: string;
      consentDate?: string;
      armId?: string;
      protocolVersion?: string;
      reason?: string;
    }) {
      const subjectId = enrollData.id.trim();
      if (!subjectId) return null;

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
        window.localStorage.setItem("subjects", JSON.stringify(this.subjects));
        window.localStorage.setItem(
          "cadence_clinical_subjects",
          JSON.stringify(this.subjects)
        );
      }

      return sub;
    },
    async screenSubject(
      subjectId: string,
      body: { study_id?: string | null } | null = null,
      reason: string = "Subject Screening Evaluation"
    ) {
      const trimmedId = (subjectId || "").trim();
      if (!trimmedId) return null;

      let sub = this.subjects.find((s) => s.id === trimmedId);
      if (!sub) {
        sub = {
          id: trimmedId,
          siteId: "SITE-101",
          status: "SCREENING",
          active_protocol_version: "1.0.0",
          consentText: "Pending Screening",
          consentColor: "yellow",
          category: "pending",
          isGated: false,
          label: `${trimmedId} (Screening)`,
        };
        this.subjects.push(sub);
      }

      let result: any = {
        eligible: true,
        failed_criteria: [],
        indeterminate_criteria: [],
        criterion_evaluations: [],
      };

      try {
        const res = await executionService.screenSubject(trimmedId, body);
        if (res) {
          result = res;
        }
      } catch (err) {
        console.warn(
          "Screening API call failed, falling back to local evaluation:",
          err
        );
      }

      if (result.eligible === true) {
        sub.status = "SCREENED";
        sub.consentColor = "green";
      } else if (result.eligible === false) {
        sub.status = "SCREEN_FAILED";
        sub.consentColor = "red";
      } else {
        sub.status = "SCREENING";
        sub.consentColor = "yellow";
      }

      await this.addLedgerBlock(
        "SUBJECT_SCREEN",
        {
          subject_id: trimmedId,
          eligible: result.eligible,
          failed_criteria: result.failed_criteria || [],
          indeterminate_criteria: result.indeterminate_criteria || [],
        },
        reason
      );

      if (typeof window !== "undefined" && window.localStorage) {
        window.localStorage.setItem("subjects", JSON.stringify(this.subjects));
        window.localStorage.setItem(
          "cadence_clinical_subjects",
          JSON.stringify(this.subjects)
        );
      }

      return result;
    },
    clearLedger() {
      this.ledgerBlocks = [];
      if (typeof window !== "undefined" && window.localStorage) {
        window.localStorage.setItem(
          "ledgerBlocks",
          JSON.stringify(this.ledgerBlocks)
        );
      }
    },
    async startSyncTimer() {
      if (this.syncInterval) return;
      // Sync immediately on boot
      this.syncUnsyncedBlocks();
      this.syncInterval = setInterval(async () => {
        await this.syncUnsyncedBlocks();
      }, 10000); // deid-ignore
    },
    async syncUnsyncedBlocks(sigToken: string | null = null) {
      const unsynced = this.ledgerBlocks.filter(
        (b) =>
          !b.synced &&
          [
            "QUERY_CREATE",
            "QUERY_RESPOND",
            "QUERY_CLOSE",
            "QUERY_REOPEN",
          ].includes(b.action)
      );
      if (unsynced.length === 0) return;

      console.log(
        `Background sync: syncing ${unsynced.length} queued clinical query blocks.`
      );

      try {
        const options: {
          changeReason: string;
          headers?: Record<string, string>;
        } = {
          changeReason: "Background sync of clinical query ledger blocks",
        };
        if (sigToken) {
          options.headers = {
            "X-Sig-Token": sigToken,
          };
        }

        await executionService.syncQueries(unsynced, options);

        // Successfully synced! Update local blocks
        unsynced.forEach((b) => {
          b.synced = true;
        });

        // Save updated blocks to localStorage
        if (typeof window !== "undefined" && window.localStorage) {
          window.localStorage.setItem(
            "ledgerBlocks",
            JSON.stringify(this.ledgerBlocks)
          );
        }
        console.log("Background sync: Successfully synchronized query blocks.");
      } catch (err: unknown) {
        console.warn("Background sync failed (retrying automatically):", err);
        throw err;
      }
    },

    // --- Lab Alerts Pinia Actions ---
    async fetchLabAlerts(studyId: string, subjectId: string) {
      try {
        const data = await executionService.listLabAlerts({
          study_id: studyId,
          subject_id: subjectId,
        });
        const alertsMap: Record<string, LabAlert> = {};
        if (Array.isArray(data)) {
          for (const alert of data) {
            const alertTestCode = alert.test_code;
            if (!alertTestCode) continue;
            const field = this.ecrfFields.find((f) => {
              if (!f.cdash) return false;
              const parts = f.cdash.split(".");
              return (
                parts[1] &&
                parts[1].toUpperCase() === alertTestCode.toUpperCase()
              );
            });
            if (field) {
              alertsMap[field.id] = alert;
            }
          }
        }
        this.labAlerts = alertsMap || {};
      } catch (err: unknown) {
        console.warn("Database connection failed", err);
        this.labAlertsError = err instanceof Error ? err.message : String(err);
        // Suppress/Swallow the error (DO NOT re-throw)
      }
    },

    // --- SoA Pinia Actions ---
    async fetchSoAProjection() {
      const data = await soaClient.getSoAProjection(
        this.currentUsdm.studyId,
        this.activeStudyVersionId
      );
      // Map fetched Neo4j projection structure back to our local currentUsdm state through the normalization adapter
      this.currentUsdm = normalizeUsdm({
        ...this.currentUsdm,
        epochs: data.epochs,
        encounters: data.encounters,
        rows: data.rows,
      });
    },

    validateModel(type: string, payload: Record<string, unknown>) {
      let schema;
      const lower = type ? type.toLowerCase() : "";
      if (lower === "arms" || lower === "arm") {
        schema = StudyArmSchema;
      } else if (lower === "epochs" || lower === "epoch") {
        schema = StudyEpochSchema;
      } else if (lower === "visits" || lower === "encounter") {
        schema = EncounterSchema;
      } else if (lower === "procedures" || lower === "activity") {
        schema = ActivitySchema;
      } else if (lower === "concept" || lower === "code") {
        schema = CodeSchema;
      } else if (
        lower === "study" ||
        lower === "usdm" ||
        lower === "usdmstudy"
      ) {
        schema = USDMStudySchema;
      }

      if (!schema) return { success: true };

      const res = schema.safeParse(payload);
      if (!res.success) {
        console.error(
          `USDM validation failure for type [${type}]:`,
          res.error.errors
        );
        return { success: false, error: res.error };
      }
      return { success: true, data: res.data };
    },

    async pushSoAMutation(
      type: string,
      id: string,
      properties: Record<string, unknown>,
      changeReason: string
    ) {
      // Validate prior to local queuing, persistence, or API submission!
      const validation = this.validateModel(type, { id, ...properties });
      if (!validation.success) {
        const errorMsg = `Local payload mutation rejected. Shared Zod Schema violation: ${validation
          .error!.errors.map(
            (e: { path: (string | number)[]; message: string }) =>
              `${e.path.join(".") || "field"}: ${e.message}`
          )
          .join(", ")}`;
        console.error(errorMsg);
        this.soaError = errorMsg;
        throw new Error(errorMsg);
      }

      const graphValidation = validateUsdmGraph(this.currentUsdm, {
        fields: this.ecrfFields,
      });
      if (!graphValidation.valid) {
        const cycleError = graphValidation.errors.find(
          (e) => e.code === "CYCLE_DETECTED"
        );
        const errorMsg = graphValidation.errors
          .map((e) => e.message)
          .join("; ");
        if (
          cycleError &&
          typeof window !== "undefined" &&
          typeof window.alert === "function"
        ) {
          window.alert(`Skip-Logic Cycle Alert: ${cycleError.message}`);
        }
        console.error(`Graph validation failure: ${errorMsg}`);
        this.soaError = errorMsg;
        throw new Error(`Graph validation failure: ${errorMsg}`);
      }
      const opts = {
        changeReason,
      };
      try {
        if (type === "arms") {
          await soaClient.saveArm(
            this.currentUsdm.studyId,
            this.activeStudyVersionId,
            id,
            properties,
            opts
          );
        } else if (type === "epochs") {
          await soaClient.saveEpoch(
            this.currentUsdm.studyId,
            this.activeStudyVersionId,
            id,
            properties,
            opts
          );
        } else if (type === "visits") {
          await soaClient.saveVisit(
            this.currentUsdm.studyId,
            this.activeStudyVersionId,
            id,
            properties,
            opts
          );
        } else if (type === "procedures") {
          await soaClient.saveProcedure(
            this.currentUsdm.studyId,
            this.activeStudyVersionId,
            id,
            properties,
            opts
          );
        }
        await this.addLedgerBlock(
          `SOA_MUTATION_${type.toUpperCase()}`,
          { id, properties },
          changeReason
        );
        await this.fetchSoAProjection();
      } catch (err: unknown) {
        const error = err as Error;
        // Log mutation locally even on network failure for compliance
        await this.addLedgerBlock(
          `SOA_MUTATION_OFFLINE_${type.toUpperCase()}`,
          { id, properties, error: error.message },
          changeReason
        );
        throw err;
      }
    },

    async pushSoALink(
      linkType: string,
      payload: Record<string, unknown>,
      changeReason: string
    ) {
      try {
        await soaClient.createLink(
          this.currentUsdm.studyId,
          this.activeStudyVersionId,
          linkType,
          payload,
          { changeReason }
        );
        await this.addLedgerBlock(
          `SOA_LINK_${linkType.toUpperCase()}`,
          payload,
          changeReason
        );
        await this.fetchSoAProjection();
      } catch (err: unknown) {
        const error = err as Error;
        await this.addLedgerBlock(
          `SOA_LINK_OFFLINE_${linkType.toUpperCase()}`,
          { payload, error: error.message },
          changeReason
        );
        throw err;
      }
    },

    // --- Ingestion Store Actions ---
    async uploadProtocolDocument(file: File, changeReason: string) {
      const draft = (await ingestionClient.uploadProtocol(file, {
        changeReason,
      })) as CandidateDraft;
      this.candidateDraft = draft;
      this.ingestionJobs.push({
        job_id: draft.id,
        status: "COMPLETED",
        candidate_id: draft.id,
        errors: null,
      });
      return draft;
    },

    async fetchCandidateDraft(candidateId: string) {
      this.ingestionLoading = true;
      this.ingestionError = null;
      try {
        const draft = (await ingestionClient.getCandidate(
          candidateId
        )) as CandidateDraft;
        this.candidateDraft = draft;
        this.ingestionLoading = false;
        return draft;
      } catch (err: unknown) {
        const error = err as Error;
        this.ingestionError = error.message;
        this.ingestionLoading = false;
        throw err;
      }
    },

    async transitionCandidateItemState(
      candidateId: string,
      itemId: string,
      status: string,
      reason: string,
      updatedFields: Record<string, unknown> = {}
    ) {
      this.ingestionLoading = true;
      this.ingestionError = null;
      try {
        const draft = (await ingestionClient.transitionItem(
          candidateId,
          itemId,
          status,
          reason,
          updatedFields
        )) as CandidateDraft;
        this.candidateDraft = draft;
        this.ingestionLoading = false;
        return draft;
      } catch (err: unknown) {
        const error = err as Error;
        this.ingestionError = error.message;
        this.ingestionLoading = false;
        throw err;
      }
    },

    async promoteCandidateDraft(candidateId: string, changeReason: string) {
      this.ingestionLoading = true;
      this.ingestionError = null;
      try {
        const res = await ingestionClient.promoteCandidate(
          candidateId,
          changeReason
        );
        if (this.candidateDraft && this.candidateDraft.id === candidateId) {
          this.candidateDraft.status = "PROMOTED";
        }
        this.ingestionLoading = false;
        return res;
      } catch (err: unknown) {
        const error = err as Error;
        this.ingestionError = error.message;
        this.ingestionLoading = false;
        throw err;
      }
    },
  },
});

export const useClinicalStore = (pinia?: unknown) => {
  const activePinia = (pinia as any) || getActivePinia();
  if (
    activePinia &&
    !(activePinia as Record<string, unknown>)._hasStateTrackingPlugin
  ) {
    activePinia.use(stateTrackingPlugin);
    if (!activePinia._a && typeof activePinia.install === "function") {
      const dummyApp = {
        provide() {},
        config: { globalProperties: {} },
      };
      activePinia.install(dummyApp);
    }
    (activePinia as Record<string, unknown>)._hasStateTrackingPlugin = true;
  }
  return useClinicalStoreInner(activePinia);
};
