import { computed, getCurrentInstance, ref, watch, type Ref } from "vue";
import type { RouteLocationRaw, Router } from "vue-router";
import { getActivePinia } from "pinia";
import { kernelApi, type KernelObservation } from "../api/kernelApi";
import {
  collectionInstanceId,
  collectionInstanceRef,
  formatNodeRef,
  observationNodeRef,
  parseNodeRef,
  tryNodeRef,
} from "../canvas/nodeRef";
import { seedNodeSummary } from "../canvas/nodeResolver";
import { buildStudiesWorkspacePath } from "../canvas/palette";
import { seedKernelStudyRef } from "../canvas/kernelStudy";
import { syncActiveStudyScope } from "./useActiveStudyScope";
import { useKernelStudyRef } from "./useKernelStudyRef";
import { isDemoBuild } from "../utils/demoMode";

export interface ObservationScopeProps {
  observationRef: string;
  studyCode?: string | null;
  studyRef?: string | null;
  embedded?: boolean;
  returnTo?: RouteLocationRaw | null;
  returnLabel?: string;
}

function appendFormAndItemToReturnPath(
  rawPath: string,
  formVal: string,
  itemVal: string
): string {
  if (!formVal && !itemVal) return rawPath;
  try {
    const parsed = new URL(rawPath, "https://cadence.local");
    const formUuid = collectionInstanceId(formVal);
    const formRef = collectionInstanceRef(formVal) ?? formVal;
    if (parsed.pathname === "/ecrf") {
      if (formUuid && !parsed.searchParams.has("formId")) {
        parsed.searchParams.set("formId", formUuid);
      }
      if (itemVal && !parsed.searchParams.has("itemId")) {
        parsed.searchParams.set("itemId", itemVal);
      }
    } else if (parsed.pathname.startsWith("/studies/")) {
      if (formRef && !parsed.searchParams.has("form")) {
        parsed.searchParams.set("form", formRef);
      }
      if (itemVal && !parsed.searchParams.has("item")) {
        parsed.searchParams.set("item", itemVal);
      }
    }
    return `${parsed.pathname}${parsed.search}${parsed.hash}`;
  } catch {
    return rawPath;
  }
}

function resolveCanonicalParticipantParam(
  studyCodeValue: string,
  rawCandidate: string
): string {
  const candidate = rawCandidate.trim();
  if (!candidate) return "";
  const parsed = parseNodeRef(candidate);
  if (parsed?.kind === "enrollment" || parsed?.kind === "participant") {
    return formatNodeRef({ kind: parsed.kind, id: parsed.id });
  }
  const stateRoot = getActivePinia()?.state.value as
    | {
        studyCanvas?: {
          studyCode?: string;
          knownParticipants?: Record<
            string,
            {
              id?: string;
              subject_id?: string;
              study_id?: string;
              enrollment_ref?: string | null;
            }
          >;
        };
      }
    | undefined;
  const knownParticipants = stateRoot?.studyCanvas?.knownParticipants;
  if (knownParticipants) {
    const matched = Object.values(knownParticipants).find(
      (item) =>
        item?.subject_id === candidate &&
        (!item.study_id ||
          item.study_id === studyCodeValue ||
          stateRoot?.studyCanvas?.studyCode === studyCodeValue)
    );
    const enrollmentNode = parseNodeRef(matched?.enrollment_ref);
    if (enrollmentNode?.kind === "enrollment") {
      return formatNodeRef({ kind: "enrollment", id: enrollmentNode.id });
    }
    const node = matched?.id ? tryNodeRef("participant", matched.id) : null;
    if (node) {
      return formatNodeRef(node);
    }
  }
  return candidate;
}

function fallbackStepCode(rawLabel: string): string {
  const trimmed = rawLabel.trim();
  if (!trimmed) return "";
  const head = /\s+[—–-]\s+/.test(trimmed)
    ? trimmed.split(/\s+[—–-]\s+/)[0].trim()
    : trimmed;
  return head && !/\s/.test(head) ? head : "";
}

function extractStepStudyRef(step: unknown): string | null {
  if (!step || typeof step !== "object") return null;
  const s = step as { ref?: unknown; node?: unknown };
  if (typeof s.ref === "string" && s.ref.trim()) return s.ref.trim();
  if (typeof s.node === "string" && s.node.trim()) return s.node.trim();
  if (s.node && typeof s.node === "object") {
    const n = s.node as { kind?: unknown; id?: unknown };
    const parsed =
      typeof n.kind === "string" && n.kind === "study"
        ? tryNodeRef("study", n.id)
        : null;
    return parsed ? formatNodeRef(parsed) : null;
  }
  return null;
}

async function resolveCanonicalStepCode(
  step:
    | {
        ref?: string | null;
        node?: unknown;
        label?: string | null;
        mask?: unknown;
      }
    | undefined
): Promise<string> {
  if (!step || step.mask !== null) return "";
  const fallbackLabel = typeof step.label === "string" ? step.label.trim() : "";
  const stepRef =
    typeof step.ref === "string" && step.ref.trim() ? step.ref.trim() : "";
  if (!stepRef) return fallbackStepCode(fallbackLabel);
  const expectedKind = parseNodeRef(stepRef)?.kind ?? stepRef.split(":")[0];
  if (typeof kernelApi.getNode === "function") {
    try {
      const summary = await kernelApi.getNode(stepRef);
      if (summary && typeof summary === "object") {
        if (
          (summary.ref && summary.ref !== stepRef) ||
          (expectedKind && summary.kind && summary.kind !== expectedKind)
        ) {
          return fallbackStepCode(fallbackLabel);
        }
        if (summary.mask !== null && summary.mask !== undefined) {
          return "";
        }
        seedNodeSummary(stepRef, {
          kind: summary.kind,
          code: summary.code,
          title: summary.title,
          status: summary.status,
        });
        if (typeof summary.code === "string") {
          return summary.code.trim();
        }
        if ("code" in summary && summary.code === null) {
          return "";
        }
      }
    } catch (err) {
      const status = (err as { status?: number | null } | null)?.status;
      if (status === 401 || status === 403 || status === 404) {
        return "";
      }
      return fallbackStepCode(fallbackLabel);
    }
  }
  return fallbackStepCode(fallbackLabel);
}

export function useObservationScopeNavigation(options: {
  props: ObservationScopeProps;
  observation: Ref<KernelObservation | null>;
  isDirty: Ref<boolean>;
  onLoaded: (obs: KernelObservation) => void;
}) {
  const { props, observation, isDirty, onLoaded } = options;
  const instance = getCurrentInstance();
  const activeRouter = (instance?.appContext.config.globalProperties.$router ??
    null) as Router | null;
  const routerLinkTag = instance?.appContext.components.RouterLink ?? "a";
  const hasRouter = computed(() => activeRouter !== null);

  let redirectRequest = 0;
  const resolvedSiteLabel = ref("");
  const resolvedParticipantLabel = ref("");

  function queryString(name: string): string {
    const raw = activeRouter?.currentRoute.value?.query?.[name];
    return typeof raw === "string" ? raw.trim() : "";
  }

  const { studyRef: observationStudyRef } = useKernelStudyRef(
    () => props.studyCode || queryString("studyId") || queryString("study"),
    {
      studyRef: () => props.studyRef,
      embedded: () => props.embedded,
    }
  );

  const effectiveReturnTo = computed<RouteLocationRaw | null>(() => {
    if (props.returnTo) return props.returnTo;
    if (!activeRouter) return null;
    const rawForm = queryString("form") || queryString("formId");
    const rawItem = queryString("item") || queryString("itemId");
    const queryReturn = activeRouter.currentRoute.value?.query?.returnTo;
    if (
      typeof queryReturn === "string" &&
      queryReturn.startsWith("/") &&
      !queryReturn.startsWith("//")
    ) {
      return appendFormAndItemToReturnPath(queryReturn, rawForm, rawItem);
    }
    const studyFromContext =
      (props.studyCode && props.studyCode.trim()) ||
      queryString("studyId") ||
      queryString("study");
    const formRef = rawForm ? (collectionInstanceRef(rawForm) ?? rawForm) : "";
    const formUuid = rawForm ? collectionInstanceId(rawForm) : null;
    if (studyFromContext && activeRouter.hasRoute("study")) {
      const sectionVal = queryString("section");
      const isDataWorklist =
        sectionVal === "queries" || sectionVal === "validation";
      const siteVal =
        queryString("site") || queryString("siteId") || resolvedSiteLabel.value;
      const participantVal = resolveCanonicalParticipantParam(
        studyFromContext,
        queryString("participant") ||
          queryString("subjectId") ||
          queryString("subject") ||
          resolvedParticipantLabel.value
      );
      return {
        name: "study",
        params: { studyCode: studyFromContext },
        query: {
          stage: isDataWorklist ? "data" : queryString("stage") || "conduct",
          ...(isDataWorklist ? { section: sectionVal } : {}),
          ...(siteVal ? { site: siteVal } : {}),
          ...(participantVal ? { participant: participantVal } : {}),
          ...(formRef ? { form: formRef } : {}),
          ...(rawItem ? { item: rawItem } : {}),
        },
      };
    }
    if ((formUuid || rawItem) && activeRouter.hasRoute("ecrf")) {
      return {
        name: "ecrf",
        query: {
          ...(formUuid ? { formId: formUuid } : {}),
          ...(rawItem ? { itemId: rawItem } : {}),
        },
      };
    }
    if (activeRouter.hasRoute("studies")) {
      return { name: "studies" };
    }
    if (activeRouter.hasRoute("study")) {
      return buildStudiesWorkspacePath({
        studyCode: studyFromContext,
        siteId:
          queryString("site") ||
          queryString("siteId") ||
          resolvedSiteLabel.value,
        subjectCode:
          queryString("subjectId") ||
          queryString("subject") ||
          resolvedParticipantLabel.value,
      });
    }
    return "/";
  });

  const effectiveReturnLabel = computed(() => {
    if (props.returnTo) return props.returnLabel || "Back to study";
    const queryLabel = activeRouter?.currentRoute.value?.query?.returnLabel;
    if (typeof queryLabel === "string" && queryLabel.trim().length > 0) {
      return queryLabel.trim();
    }
    const rawForm = queryString("form") || queryString("formId");
    if (rawForm) return props.returnLabel || "Back to form";
    return props.returnLabel || "Back to studies";
  });

  const effectiveStudyCode = computed(
    () =>
      (props.studyCode && props.studyCode.trim()) ||
      queryString("studyId") ||
      queryString("study") ||
      ""
  );

  const effectiveFormRef = computed(() => {
    const fromObs = observation.value?.form_ref?.trim() || "";
    if (fromObs) return collectionInstanceRef(fromObs) ?? fromObs;
    const fromQuery = queryString("form") || queryString("formId");
    return fromQuery ? (collectionInstanceRef(fromQuery) ?? fromQuery) : "";
  });

  const effectiveItemId = computed(
    () =>
      observation.value?.item_id?.trim() ||
      queryString("item") ||
      queryString("itemId") ||
      ""
  );

  const owningFormLocation = computed<RouteLocationRaw | null>(() => {
    if (!activeRouter || !effectiveFormRef.value) return null;
    const studyVal = effectiveStudyCode.value;
    const siteVal =
      queryString("site") || queryString("siteId") || resolvedSiteLabel.value;
    if (studyVal && activeRouter.hasRoute("study")) {
      const participantVal = resolveCanonicalParticipantParam(
        studyVal,
        queryString("participant") ||
          queryString("subjectId") ||
          queryString("subject") ||
          resolvedParticipantLabel.value
      );
      return {
        name: "study",
        params: { studyCode: studyVal },
        query: {
          stage: "conduct",
          ...(siteVal ? { site: siteVal } : {}),
          ...(participantVal ? { participant: participantVal } : {}),
          form: effectiveFormRef.value,
          ...(effectiveItemId.value ? { item: effectiveItemId.value } : {}),
        },
      };
    }
    const formUuid = collectionInstanceId(effectiveFormRef.value);
    if (formUuid && activeRouter.hasRoute("ecrf")) {
      return {
        name: "ecrf",
        query: {
          formId: formUuid,
          ...(effectiveItemId.value ? { itemId: effectiveItemId.value } : {}),
        },
      };
    }
    return null;
  });

  const studyQueriesLocation = computed<RouteLocationRaw | null>(() => {
    const studyVal = effectiveStudyCode.value;
    if (!activeRouter || !studyVal || !activeRouter.hasRoute("study")) {
      return null;
    }
    const siteVal =
      queryString("site") || queryString("siteId") || resolvedSiteLabel.value;
    return {
      name: "study",
      params: { studyCode: studyVal },
      query: {
        stage: "data",
        section: "queries",
        ...(siteVal ? { site: siteVal } : {}),
      },
    };
  });

  watch(
    observation,
    async (obs) => {
      if (!obs) return;
      onLoaded(obs);
      if (
        props.embedded ||
        isDirty.value ||
        !activeRouter ||
        activeRouter.currentRoute.value.name !== "observation" ||
        typeof kernelApi.getZoomPath !== "function"
      ) {
        return;
      }
      const explicitReturn = activeRouter.currentRoute.value.query?.returnTo;
      if (
        typeof explicitReturn === "string" &&
        explicitReturn.startsWith("/") &&
        !explicitReturn.startsWith("//")
      ) {
        const explicitStudyFromQuery =
          queryString("studyId") || queryString("study");
        if (
          !props.studyRef &&
          !props.studyCode &&
          !(isDemoBuild() && explicitStudyFromQuery) &&
          typeof kernelApi.getZoomPath === "function"
        ) {
          const request = ++redirectRequest;
          try {
            const steps = await kernelApi.getZoomPath(obs.enrollment_ref);
            if (request !== redirectRequest) return;
            const studyStep = steps.find(
              (s) => s.level === "study" && s.mask === null
            );
            observationStudyRef.value = extractStepStudyRef(studyStep);
          } catch {
            // Keep standalone observation view when zoom path is unavailable.
          }
        }
        return;
      }
      const request = ++redirectRequest;
      try {
        const steps = await kernelApi.getZoomPath(obs.enrollment_ref);
        if (request !== redirectRequest) return;
        const studyStep = steps.find(
          (s) => s.level === "study" && s.mask === null
        );
        const studyRef = extractStepStudyRef(studyStep);
        const explicitStudyFromQuery =
          queryString("studyId") || queryString("study");
        if (
          !props.studyRef &&
          !props.studyCode &&
          !(isDemoBuild() && explicitStudyFromQuery)
        ) {
          observationStudyRef.value = studyRef;
        }
        const enrollmentStep = steps.find(
          (s) => s.level === "enrollment" && s.mask === null
        );
        if (!enrollmentStep) return;
        const siteStep = steps.find(
          (s) =>
            (s.level === "study_site" || s.level === "site") && s.mask === null
        );
        const [lineageStudyCode, canonicalSiteCode] = await Promise.all([
          studyStep ? resolveCanonicalStepCode(studyStep) : Promise.resolve(""),
          siteStep ? resolveCanonicalStepCode(siteStep) : Promise.resolve(""),
        ]);
        if (request !== redirectRequest) return;
        const demoMode = isDemoBuild();
        const explicitStudy = queryString("studyId") || queryString("study");
        const explicitSite = queryString("site") || queryString("siteId");
        const explicitParticipant =
          queryString("participant") ||
          queryString("subjectId") ||
          queryString("subject");
        const canonicalStudyCode = demoMode
          ? explicitStudy || lineageStudyCode
          : lineageStudyCode || explicitStudy;
        if (!canonicalStudyCode) return;
        syncActiveStudyScope(canonicalStudyCode);
        const studyOverridden = Boolean(
          demoMode &&
          explicitStudy &&
          lineageStudyCode &&
          explicitStudy.toUpperCase() !== lineageStudyCode.toUpperCase()
        );
        if (studyRef && !studyOverridden) {
          seedKernelStudyRef(canonicalStudyCode, studyRef);
        }
        const siteVal = demoMode
          ? explicitSite || canonicalSiteCode
          : canonicalSiteCode || explicitSite;
        const siteOverridden = Boolean(
          demoMode &&
          explicitSite &&
          canonicalSiteCode &&
          explicitSite.toUpperCase() !== canonicalSiteCode.toUpperCase()
        );
        resolvedSiteLabel.value = siteVal || "";
        resolvedParticipantLabel.value =
          studyOverridden || siteOverridden
            ? explicitParticipant
            : enrollmentStep.label || "";
        const participantCandidate =
          explicitParticipant ||
          (studyOverridden || siteOverridden ? "" : enrollmentStep.label || "");
        const participantVal = resolveCanonicalParticipantParam(
          canonicalStudyCode,
          participantCandidate
        );
        const rawForm = queryString("form") || queryString("formId");
        const formRef = rawForm
          ? (collectionInstanceRef(rawForm) ?? rawForm)
          : "";
        const rawItem = queryString("item") || queryString("itemId");
        const sectionVal = queryString("section");
        const isDataWorklist =
          sectionVal === "queries" || sectionVal === "validation";
        if (
          isDirty.value ||
          activeRouter.currentRoute.value.name !== "observation"
        ) {
          return;
        }
        const canonicalRef =
          observationNodeRef(obs.ref) ?? props.observationRef;
        await activeRouter.replace({
          name: "study",
          params: { studyCode: canonicalStudyCode },
          query: {
            stage: isDataWorklist ? "data" : queryString("stage") || "conduct",
            ...(isDataWorklist ? { section: sectionVal } : {}),
            ...(siteVal ? { site: siteVal } : {}),
            ...(participantVal ? { participant: participantVal } : {}),
            ...(formRef ? { form: formRef } : {}),
            ...(rawItem ? { item: rawItem } : {}),
            observation: canonicalRef,
          },
        });
      } catch {
        // Keep the standalone view if scope-path resolution is unauthorized or unavailable.
      }
    },
    { immediate: true }
  );

  return {
    routerLinkTag,
    hasRouter,
    effectiveReturnTo,
    effectiveReturnLabel,
    observationStudyRef,
    effectiveItemId,
    owningFormLocation,
    studyQueriesLocation,
  };
}
