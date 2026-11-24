import { DEMO_STUDY } from "demo-fixtures";
import { soaClient } from "../../api/soaClient.js";
import { isDemoBuild } from "../../utils/demoMode.js";
import { normalizeUsdm, type NormalizedUsdm } from "../normalization";
import { useToastStore } from "../toasts.js";
import { buildUsdmSoaSchedule } from "./usdmSoaSchedule";
import type {
  ClinicalState,
  LedgerBlock,
  MutationValidationInput,
  ZodErrorLike,
  ZodIssueLike,
} from "../clinicalScope";
import type * as UsdmSchemasModule from "usdm-schemas";

export type UsdmSchemas = Pick<
  typeof UsdmSchemasModule,
  | "CodeSchema"
  | "ActivitySchema"
  | "EncounterSchema"
  | "StudyArmSchema"
  | "StudyEpochSchema"
  | "USDMV3StudySchema"
  | "USDMStudySchema"
  | "WrapperSchema"
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
  ...buildUsdmSoaSchedule(),
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

export async function assertValidUsdmGraph(
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
    if (cycleError) {
      useToastStore().push({
        tone: "error",
        title: "Skip-Logic Cycle Alert",
        message: cycleError.message,
        timeoutMs: 0,
      });
    }
    const fullMessage = `${errorPrefix}: ${errorMsg}`;
    console.error(fullMessage);
    state.soaError = errorMsg;
    throw new Error(fullMessage);
  }
}

export function createClinicalUsdmState(
  savedUsdm: unknown,
  demo: boolean = isDemoBuild()
): Pick<
  ClinicalState,
  "currentUsdm" | "activeStudyVersionId" | "soaLoading" | "soaError"
> {
  return {
    currentUsdm: savedUsdm
      ? normalizeUsdm(savedUsdm)
      : cloneDefaultClinicalUsdm(demo),
    activeStudyVersionId: "v_draft_01",
    soaLoading: false,
    soaError: null,
  };
}

type UsdmStoreContext = ClinicalState & {
  addLedgerBlock: (
    action: string,
    details: Record<string, unknown>,
    reason?: string
  ) => Promise<LedgerBlock>;
  fetchSoAProjection: () => Promise<void>;
  validateModel: (
    type: string,
    payload: Record<string, unknown>
  ) => { success: boolean; data?: unknown; error?: ZodErrorLike };
};

export function createClinicalUsdmActions() {
  return {
    async fetchSoAProjection(this: UsdmStoreContext) {
      const data = await soaClient.getSoAProjection(
        this.currentUsdm.studyId,
        this.activeStudyVersionId
      );
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
      } else if (lower === "wrapper" || lower === "usdmwrapper") {
        schema = schemas.WrapperSchema;
      } else if (lower === "usdmv3" || lower === "studyv3") {
        schema = schemas.USDMV3StudySchema;
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
      this: UsdmStoreContext,
      type: string,
      id: string,
      properties: Record<string, unknown>,
      changeReason: string
    ) {
      await ensureUsdmSchemas();
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
        await this.addLedgerBlock(
          `SOA_MUTATION_OFFLINE_${type.toUpperCase()}`,
          { id, properties, error: error.message },
          changeReason
        );
        throw err;
      }
    },

    async pushSoALink(
      this: UsdmStoreContext,
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
  };
}
