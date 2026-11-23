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
import {
  DEFAULT_DEMO_PARTICIPANT,
  DEFAULT_DEMO_SITE,
  DEMO_PARTICIPANTS,
  DEMO_SITES,
  DEMO_STUDY,
  getDemoPersonaByWorkflow,
} from "demo-fixtures";
import { useAuthStore } from "./auth.js";
import { useToastStore } from "./toasts.js";
import { getSyncedStudyScope, syncActiveStudyScope } from "./studyScopeBridge";
import {
  allowUnbackedAction,
  refuseUnbackedAction,
} from "../utils/unbackedActions.js";
import { demoNowIso, isDemoBuild } from "../utils/demoMode.js";
import { scopedKey, storageScopeOf } from "../utils/userScopedStorage.js";
import { executionApi, type SubjectSummary } from "../api/executionApi";
import { soaClient } from "../api/soaClient.js";
import { evaluateAST } from "../evaluator.js";
import { ingestionClient } from "../api/ingestionClient.js";
import { normalizeUsdm, type NormalizedUsdm } from "./normalization";
import type * as UsdmSchemasModule from "usdm-schemas";

type UsdmSchemas = Pick<
  typeof UsdmSchemasModule,
  | "CodeSchema"
  | "ActivitySchema"
  | "EncounterSchema"
  | "StudyArmSchema"
  | "StudyEpochSchema"
  | "USDMStudySchema"
  | "validateUsdmGraph"
>;

let cachedUsdmSchemas: UsdmSchemas | null =
  (globalThis as { __CADENCE_USDM_SCHEMAS__?: UsdmSchemas })
    .__CADENCE_USDM_SCHEMAS__ ?? null;
let usdmSchemasPromise: Promise<UsdmSchemas> | null = null;

export function registerUsdmSchemas(schemas: UsdmSchemas): void {
  cachedUsdmSchemas = schemas;
}

export async function ensureUsdmSchemas(): Promise<UsdmSchemas> {
  if (cachedUsdmSchemas) return cachedUsdmSchemas;
  if (!usdmSchemasPromise) {
    usdmSchemasPromise = import("usdm-schemas")
      .then((mod) => {
        cachedUsdmSchemas = mod;
        return mod;
      })
      .finally(() => {
        usdmSchemasPromise = null;
      });
  }
  return usdmSchemasPromise;
}

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
  /** The subject code shown to users (Execution's `subject_id`). */
  id: string;
  /** Execution's record id for the subject; absent on demo sample subjects. */
  recordId?: string;
  siteId?: string;
  site_id?: string;
  consentDate?: string;
  armId?: string;
  status: string;
  active_protocol_version: string;
  consentText?: string;
  consentColor?: string;
  category?: string;
  isGated: boolean;
  label?: string;
}

/** Maps an Execution subject summary onto the eCRF's subject shape. */
export function subjectFromExecution(summary: SubjectSummary): SubjectFixture {
  const isGated = summary.status === "RECONSENT_REQUIRED";
  return {
    id: summary.subject_id,
    recordId: summary.id,
    siteId: summary.site_id ?? undefined,
    status: summary.status,
    active_protocol_version: summary.active_protocol_version,
    isGated,
    consentText: isGated ? "Re-consent required" : undefined,
    consentColor: isGated ? "yellow" : undefined,
  };
}

export interface ClinicalState {
  currentUsdm: NormalizedUsdm;
  currentCtmsData: CtmsData;
  subjects: SubjectFixture[];
  /** Subjects in the active study on the server; the list holds the first page (#4828). */
  subjectsTotal: number;
  subjectsLoading: boolean;
  subjectsError: string | null;
  ecrfFields: ClinicalField[];
  formValues: Record<string, string | number | boolean | null | undefined>;
  fieldVisibility: Record<string, boolean>;
  formQueries: Record<string, ClinicalQuery>;
  ledgerBlocks: LedgerBlock[];

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

export function getCurrentPersonaKey(): string {
  if (typeof window !== "undefined") {
    try {
      const authStore = useAuthStore();
      if (authStore && authStore.currentPersona) {
        return authStore.currentPersona;
      }
    } catch {
      // Pinia or auth store may not be initialized yet
    }
  }
  return "super_admin";
}

function currentStorageScope() {
  try {
    return storageScopeOf(useAuthStore());
  } catch {
    // Pinia or auth store may not be initialized yet
    return null;
  }
}

/**
 * The browser storage key for one persisted clinical field of the signed-in
 * account: `cadence_user:<tenant>:<user>:clinical:<persona>:<baseKey>`. Null
 * when no account with a tenant and user id is signed in (#5369), so callers
 * neither read nor write.
 */
export function getStorageKey(
  baseKey: string,
  personaKey?: string
): string | null {
  const persona = personaKey || getCurrentPersonaKey();
  return scopedKey(currentStorageScope(), "clinical", persona, baseKey);
}

export function readStorageItem(
  baseKey: string,
  personaKey?: string
): string | null {
  if (typeof window === "undefined" || !window.localStorage) return null;
  const key = getStorageKey(baseKey, personaKey);
  return key ? window.localStorage.getItem(key) : null;
}

/** Writes one persisted clinical field for the signed-in account, if any. */
function writeStorageItem(baseKey: string, value: unknown): void {
  if (typeof window === "undefined" || !window.localStorage) return;
  const key = getStorageKey(baseKey);
  if (key) window.localStorage.setItem(key, JSON.stringify(value));
}

export const DEFAULT_CLINICAL_USDM = Object.freeze({
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
});

function buildDemoClinicalUsdm() {
  return {
    ...DEFAULT_CLINICAL_USDM,
    studyId: DEMO_STUDY.code || DEFAULT_CLINICAL_USDM.studyId,
    studyTitle: DEMO_STUDY.title || DEFAULT_CLINICAL_USDM.studyTitle,
  };
}

export function cloneDefaultClinicalUsdm(
  demo: boolean = isDemoBuild()
): NormalizedUsdm {
  return normalizeUsdm(
    JSON.parse(
      JSON.stringify(demo ? buildDemoClinicalUsdm() : DEFAULT_CLINICAL_USDM)
    )
  );
}

export interface ZodIssueLike {
  path?: ReadonlyArray<PropertyKey>;
  message: string;
}

export interface ZodErrorLike {
  issues?: ReadonlyArray<ZodIssueLike>;
  errors?: ReadonlyArray<ZodIssueLike>;
}

export function formatZodIssues(
  errorOrIssues: ZodErrorLike | ReadonlyArray<ZodIssueLike> | null | undefined,
  fallbackPath: string = "field"
): string[] {
  if (!errorOrIssues) return [];
  const issueList = Array.isArray(errorOrIssues)
    ? errorOrIssues
    : (errorOrIssues as ZodErrorLike).issues ||
      (errorOrIssues as ZodErrorLike).errors ||
      [];
  return issueList.map((issue) => {
    const path = Array.isArray(issue.path)
      ? issue.path.map(String).join(".")
      : "";
    return `${path || fallbackPath}: ${issue.message}`;
  });
}

export interface MutationValidationInput {
  type?: string;
  id?: string;
  properties?: Record<string, unknown>;
}

export function buildMutationValidationPayload(
  mutation: MutationValidationInput | null | undefined
): Record<string, unknown> | null {
  if (!mutation || typeof mutation.type !== "string") {
    return null;
  }
  const props = mutation.properties ?? {};
  const lower = mutation.type.toLowerCase();
  if (
    lower === "arms" ||
    lower === "arm" ||
    lower === "epochs" ||
    lower === "epoch" ||
    lower === "visits" ||
    lower === "encounter" ||
    lower === "procedures" ||
    lower === "activity"
  ) {
    return { id: mutation.id, ...props };
  }
  if (lower === "concept" || lower === "code") {
    return {
      code: mutation.id,
      codeSystem: props.codeSystem || "NCI_Thesaurus",
      codeSystemVersion: props.codeSystemVersion || "24.03d",
      decode: props.decode || props.preferred_name || "",
    };
  }
  return null;
}

async function assertValidUsdmGraph(
  state: Pick<ClinicalState, "currentUsdm" | "ecrfFields" | "soaError">,
  errorPrefix: string
): Promise<void> {
  const { validateUsdmGraph } = await ensureUsdmSchemas();
  const graphValidation = validateUsdmGraph(state.currentUsdm, {
    fields: state.ecrfFields,
  });
  if (!graphValidation.valid) {
    const cycleError = graphValidation.errors.find(
      (e) => e.code === "CYCLE_DETECTED"
    );
    const errorMsg = graphValidation.errors.map((e) => e.message).join("; ");
    if (
      cycleError &&
      typeof window !== "undefined" &&
      typeof window.alert === "function"
    ) {
      window.alert(`Skip-Logic Cycle Alert: ${cycleError.message}`);
    }
    const fullMessage = `${errorPrefix}: ${errorMsg}`;
    console.error(fullMessage);
    state.soaError = errorMsg;
    throw new Error(fullMessage);
  }
}

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

function readPersonaStorage(personaKey?: string) {
  const persona = personaKey || getCurrentPersonaKey();
  let savedSubjects: SubjectFixture[] | null = null;
  let savedFormValues: Record<
    string,
    string | number | boolean | null | undefined
  > | null = null;
  let savedFormQueries: Record<string, ClinicalQuery> | null = null;
  let savedLedgerBlocks: LedgerBlock[] | null = null;
  let savedUsdm: unknown = null;

  if (typeof window !== "undefined" && window.localStorage) {
    try {
      // Unscoped keys from older builds have no owner and are never read (#5369).
      const storedSubjects = readStorageItem("subjects", persona);
      if (storedSubjects && isDemoBuild()) {
        savedSubjects = JSON.parse(storedSubjects);
      }

      const storedFormValues = readStorageItem("formValues", persona);
      if (storedFormValues) {
        savedFormValues = JSON.parse(storedFormValues);
      }

      const storedFormQueries = readStorageItem("formQueries", persona);
      if (storedFormQueries) {
        savedFormQueries = JSON.parse(storedFormQueries);
      }

      const storedLedgerBlocks = readStorageItem("ledgerBlocks", persona);
      if (storedLedgerBlocks) {
        savedLedgerBlocks = JSON.parse(storedLedgerBlocks);
      }

      const storedUsdm = readStorageItem("currentUsdm", persona);
      if (storedUsdm) {
        savedUsdm = JSON.parse(storedUsdm);
      }
    } catch (e) {
      console.error("Failed to parse saved state from localStorage", e);
    }
  }

  return {
    savedSubjects,
    savedFormValues,
    savedFormQueries,
    savedLedgerBlocks,
    savedUsdm,
  };
}

const useClinicalStoreInner = defineStore("clinical", {
  state: (): ClinicalState => {
    const initial = readPersonaStorage();
    const demo = isDemoBuild();
    const authInstance = (
      getActivePinia() as
        | {
            _s?: Map<
              string,
              { isAuthenticated?: boolean; isDemoMode?: boolean }
            >;
          }
        | undefined
    )?._s?.get("auth");
    const useDemoDefaults =
      demo && !(authInstance?.isAuthenticated && !authInstance?.isDemoMode);
    const monitorUsername =
      (useDemoDefaults && getDemoPersonaByWorkflow("monitor")?.username) ||
      "cra.monitor";
    const siteCodes =
      useDemoDefaults && DEMO_SITES.length > 0
        ? DEMO_SITES.map((s) => s.code)
        : ["SITE-101", "SITE-102"];
    const demoStudyCode = (useDemoDefaults && DEMO_STUDY.code) || "CADENCE-101";

    return {
      currentUsdm: initial.savedUsdm
        ? normalizeUsdm(initial.savedUsdm)
        : cloneDefaultClinicalUsdm(useDemoDefaults),
      currentCtmsData: {
        milestones: [
          {
            id: "M1",
            type: "SITE_SELECTION",
            plannedDate: "2026-01-05", // deid-ignore
            actualDate: "2026-01-05", // deid-ignore
            status: "ACHIEVED",
          },
          {
            id: "M2",
            type: "INITIATION_VISIT",
            plannedDate: "2026-01-08", // deid-ignore
            actualDate: "2026-01-09", // deid-ignore
            status: "ACHIEVED",
          },
          {
            id: "M3",
            type: "SITE_ACTIVATION",
            plannedDate: "2026-01-11", // deid-ignore
            actualDate: "",
            status: "PLANNED",
          },
          {
            id: "M4",
            type: "FIRST_SUBJECT_ENROLLED",
            plannedDate: "2026-01-12", // deid-ignore
            actualDate: "",
            status: "PLANNED",
          },
        ],
        visits: [
          {
            id: "V1",
            type: "SIV",
            scheduledDate: "2026-01-08", // deid-ignore
            actualDate: "2026-01-09", // deid-ignore
            status: "SIGNED_OFF",
            cra: monitorUsername,
          },
          {
            id: "V2",
            type: "IMV",
            scheduledDate: "2026-01-25", // deid-ignore
            actualDate: "",
            status: "SCHEDULED",
            cra: monitorUsername,
          },
        ],
        allocations: [
          {
            cra: monitorUsername,
            activeAllocations: siteCodes.length,
            sites: siteCodes,
            studies: [demoStudyCode],
          },
        ],
        recruitment: [
          {
            siteId: siteCodes[0] || "SITE-101",
            screened: 2,
            enrolled: 2,
            target: 20,
          },
          {
            siteId: siteCodes[1] || "SITE-102",
            screened: 1,
            enrolled: 1,
            target: 15,
          },
        ],
      },
      subjects: demo
        ? initial.savedSubjects ||
          JSON.parse(JSON.stringify(DEFAULT_DEMO_SUBJECTS))
        : [],
      subjectsTotal: 0,
      subjectsLoading: false,
      subjectsError: null,
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
      formValues: initial.savedFormValues || { ...DEFAULT_FORM_VALUES },
      fieldVisibility: {},
      formQueries: initial.savedFormQueries || {},
      ledgerBlocks: initial.savedLedgerBlocks || [],

      activeStudyId:
        getSyncedStudyScope() || (useDemoDefaults ? DEMO_STUDY.code : ""),
      activeSiteId: useDemoDefaults ? DEFAULT_DEMO_SITE?.code || "" : "",
      activeSubjectId: useDemoDefaults
        ? DEFAULT_DEMO_PARTICIPANT?.subjectCode || ""
        : "SUBJ-001",
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
      return {
        username: authStore.identity?.username || null,
        roles: authStore.normalizedRoles,
        authenticated: authStore.isAuthenticated,
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
    /**
     * Array of all subject IDs in store.
     */
    subjectIds: (state) => {
      return state.subjects.map((s) => s.id);
    },
    isSubjectGated: (state) => (subjectId: string) => {
      const sub = state.subjects.find((s) => s.id === subjectId);
      return sub ? sub.isGated : false;
    },
  },
  actions: {
    /**
     * Loads the first page of a study's subjects from Execution (GET /api/v1/execution/subjects).
     * A demo build keeps its sample subjects. On failure the list is left as it was and the error
     * is shown; sample subjects are never substituted.
     */
    async loadSubjects(studyId: string, limit = 200): Promise<boolean> {
      if (isDemoBuild()) return true;
      const study = studyId.trim();
      if (!study) {
        this.subjects = [];
        this.subjectsTotal = 0;
        this.subjectsError = null;
        return false;
      }
      this.activeStudyId = study;
      syncActiveStudyScope(study);
      this.subjectsLoading = true;
      this.subjectsError = null;
      try {
        const page = await executionApi.listSubjects({ studyId: study, limit });
        this.subjects = page.items.map(subjectFromExecution);
        this.subjectsTotal = Number(page.total);
        return true;
      } catch (err) {
        this.subjectsError =
          err instanceof Error ? err.message : "Could not load subjects.";
        useToastStore().apiError("Couldn't load the study's subjects", err);
        return false;
      } finally {
        this.subjectsLoading = false;
      }
    },

    loadPersonaState(personaKey?: string) {
      const targetPersona = personaKey || getCurrentPersonaKey();
      const loaded = readPersonaStorage(targetPersona);
      const demo = isDemoBuild();

      this.subjects = demo
        ? loaded.savedSubjects ||
          JSON.parse(JSON.stringify(DEFAULT_DEMO_SUBJECTS))
        : [];
      this.formValues = loaded.savedFormValues || { ...DEFAULT_FORM_VALUES };
      this.formQueries = loaded.savedFormQueries || {};
      this.ledgerBlocks = loaded.savedLedgerBlocks || [];
      this.currentUsdm = loaded.savedUsdm
        ? normalizeUsdm(loaded.savedUsdm)
        : cloneDefaultClinicalUsdm(demo);

      this.evaluateRules();
    },

    async evaluateRules() {
      let changed = true;
      let passes = 0;
      while (changed && passes < 10) {
        changed = false;
        passes++;
        for (const field of this.ecrfFields) {
          // Only an explicit false from the relevance rule hides a field.
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
      const index = this.ledgerBlocks.length;
      const timestamp = demoNowIso(index);
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
      await assertValidUsdmGraph(
        this,
        "Local storage persistence blocked by USDM Graph Validator"
      );

      this.ledgerBlocks.push(block);

      // Save persistent fields to localStorage
      if (typeof window !== "undefined" && window.localStorage) {
        writeStorageItem("subjects", this.subjects);
        writeStorageItem("formValues", this.formValues);
        writeStorageItem("formQueries", this.formQueries);
        writeStorageItem("ledgerBlocks", this.ledgerBlocks);
        writeStorageItem("currentUsdm", this.currentUsdm);
      }

      return block;
    },
    async clearReconsentGate(
      subjectId: string,
      method: string = "ECONSENT",
      reason?: string
    ) {
      // Updates the local gate only. Callers must first have the re-consent recorded by a service, or be
      // running a demo build (#4827, see useConsentGating).
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
        writeStorageItem("subjects", this.subjects);
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
    /**
     * Screening evaluates a subject's eligibility against the protocol's criteria. No Cadence service serves
     * that evaluation yet, so this refuses in every build and changes nothing: an eligibility result is never
     * made up in the browser (#4523, #4828).
     */
    screenSubject(): null {
      refuseUnbackedAction("Screening a subject");
      return null;
    },
    clearLedger() {
      this.ledgerBlocks = [];
      if (typeof window !== "undefined" && window.localStorage) {
        writeStorageItem("ledgerBlocks", this.ledgerBlocks);
      }
    },
    // --- SoA Pinia Actions ---
    async fetchSoAProjection() {
      const data = await soaClient.getSoAProjection(
        this.currentUsdm.studyId,
        this.activeStudyVersionId
      );
      // Map the fetched USDM projection structure back to our local currentUsdm state through the normalization adapter
      this.currentUsdm = normalizeUsdm({
        ...this.currentUsdm,
        epochs: data.epochs,
        encounters: data.encounters,
        rows: data.rows,
      });
    },

    validateModel(type: string, payload: Record<string, unknown>) {
      const schemas = cachedUsdmSchemas;
      if (!schemas) {
        void ensureUsdmSchemas();
        return { success: true };
      }
      let schema;
      const lower = type ? type.toLowerCase() : "";
      if (lower === "arms" || lower === "arm") {
        schema = schemas.StudyArmSchema;
      } else if (lower === "epochs" || lower === "epoch") {
        schema = schemas.StudyEpochSchema;
      } else if (lower === "visits" || lower === "encounter") {
        schema = schemas.EncounterSchema;
      } else if (lower === "procedures" || lower === "activity") {
        schema = schemas.ActivitySchema;
      } else if (lower === "concept" || lower === "code") {
        schema = schemas.CodeSchema;
      } else if (
        lower === "study" ||
        lower === "usdm" ||
        lower === "usdmstudy"
      ) {
        schema = schemas.USDMStudySchema;
      }

      if (!schema) return { success: true };

      const res = schema.safeParse(payload);
      if (!res.success) {
        console.error(
          `USDM validation failure for type [${type}]:`,
          res.error.issues
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
      await ensureUsdmSchemas();
      // Validate prior to local queuing, persistence, or API submission!
      const payloadToValidate = buildMutationValidationPayload({
        type,
        id,
        properties,
      }) ?? { id, ...properties };
      const validation = this.validateModel(type, payloadToValidate);
      if (!validation.success) {
        const errorMsg = `Local payload mutation rejected. Shared Zod Schema violation: ${formatZodIssues(
          validation.error
        ).join(", ")}`;
        console.error(errorMsg);
        this.soaError = errorMsg;
        throw new Error(errorMsg);
      }

      await assertValidUsdmGraph(this, "Graph validation failure");
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
