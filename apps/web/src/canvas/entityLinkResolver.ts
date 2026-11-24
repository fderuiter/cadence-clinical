import { getCurrentInstance } from "vue";
import { getActivePinia } from "pinia";
import type { RouteLocationRaw, Router } from "vue-router";
import { flushRegisteredDraftSources, provideEntityLinkResolver } from "ui";
import { kernelApi } from "../api/kernelApi";
import { collectionInstanceRef, parseNodeRef } from "./nodeRef";
import { ensureNodesResolved, getNodeDisplay } from "./nodeResolver";

export interface EntityTargetPayload {
  nodeRef?: string | null;
  entityType?: string | null;
  entityId?: string | null;
  fallbackLabel?: string | null;
  label?: string | null;
  entry?: Record<string, unknown> | null;
}

export interface EntityScopeFallback {
  studyCode?: string | null;
  siteId?: string | null;
  subjectId?: string | null;
}

function activeStudyCodeFromContext(
  route?: {
    name?: unknown;
    params?: Record<string, unknown>;
    query?: Record<string, unknown>;
  } | null,
  fallback?: EntityScopeFallback | null
): string {
  const fromParam =
    typeof route?.params?.studyCode === "string"
      ? route.params.studyCode.trim()
      : "";
  if (fromParam) return fromParam;

  const fromQuery =
    (typeof route?.query?.studyCode === "string" &&
      route.query.studyCode.trim()) ||
    (typeof route?.query?.studyId === "string" && route.query.studyId.trim()) ||
    "";
  if (fromQuery) return fromQuery;

  const fromFallback =
    typeof fallback?.studyCode === "string" ? fallback.studyCode.trim() : "";
  if (fromFallback) return fromFallback;

  const pinia = getActivePinia();
  const canvasCode = pinia?.state.value?.studyCanvas?.studyCode;
  if (typeof canvasCode === "string" && canvasCode.trim()) {
    return canvasCode.trim();
  }
  const scopeStudy = pinia?.state.value?.clinicalScope?.activeStudyId;
  if (typeof scopeStudy === "string" && scopeStudy.trim()) {
    return scopeStudy.trim();
  }
  return "";
}

function preservedScopeQuery(
  route?: {
    name?: unknown;
    query?: Record<string, unknown>;
  } | null,
  fallback?: EntityScopeFallback | null
): Record<string, string> {
  const out: Record<string, string> = {};
  const q = route?.query ?? {};
  const onStudyRoute = route?.name === "study";

  if (onStudyRoute) {
    for (const [k, v] of Object.entries(q)) {
      if (typeof v === "string" && v) {
        out[k] = v;
      }
    }
    if (
      !out.site &&
      typeof fallback?.siteId === "string" &&
      fallback.siteId.trim()
    ) {
      out.site = fallback.siteId.trim();
    }
    if (
      !out.participant &&
      !out.subject &&
      typeof fallback?.subjectId === "string" &&
      fallback.subjectId.trim()
    ) {
      out.participant = fallback.subjectId.trim();
    }
    return out;
  }

  const pinia = getActivePinia();
  const activeSite =
    (typeof q.site === "string" && q.site.trim()) ||
    (typeof q.siteId === "string" && q.siteId.trim()) ||
    (typeof fallback?.siteId === "string" && fallback.siteId.trim()) ||
    (typeof pinia?.state.value?.clinicalScope?.activeSiteId === "string"
      ? pinia.state.value.clinicalScope.activeSiteId.trim()
      : "");
  const activeParticipant =
    (typeof q.participant === "string" && q.participant.trim()) ||
    (typeof q.subjectId === "string" && q.subjectId.trim()) ||
    (typeof fallback?.subjectId === "string" && fallback.subjectId.trim()) ||
    "";

  if (activeSite) out.site = activeSite;
  if (activeParticipant) out.participant = activeParticipant;
  return out;
}

export function buildEntityRouteLocation(
  payload: EntityTargetPayload,
  route?: {
    name?: unknown;
    params?: Record<string, unknown>;
    query?: Record<string, unknown>;
  } | null,
  fallback?: EntityScopeFallback | null
): RouteLocationRaw | null {
  const studyCode = activeStudyCodeFromContext(route, fallback);
  const baseQuery = preservedScopeQuery(route, fallback);
  const rawNodeRef = payload.nodeRef?.trim() || "";
  const parsed = rawNodeRef ? parseNodeRef(rawNodeRef) : null;

  if (parsed) {
    const canonicalRef =
      parsed.version !== undefined
        ? `${parsed.kind}:${parsed.id}@${parsed.version}`
        : `${parsed.kind}:${parsed.id}`;

    if (parsed.kind === "observation") {
      const unversionedObs = `observation:${parsed.id}`;
      if (studyCode) {
        const nextQuery: Record<string, string> = {
          ...baseQuery,
          observation: unversionedObs,
        };
        delete nextQuery.proposal;
        return {
          name: "study",
          params: { studyCode },
          query: nextQuery,
        };
      }
      return {
        name: "observation",
        params: { observationRef: unversionedObs },
      };
    }

    if (parsed.kind === "protocol_version") {
      if (!studyCode) return null;
      return {
        name: "study",
        params: { studyCode },
        query: {
          ...baseQuery,
          stage: "design",
          section: "protocol-versions",
          node: canonicalRef,
        },
      };
    }

    if (parsed.kind === "collection_instance") {
      if (!studyCode) return null;
      const formRef = collectionInstanceRef(canonicalRef) ?? canonicalRef;
      const nextQuery: Record<string, string> = {
        ...baseQuery,
        stage: baseQuery.stage || "conduct",
        section: baseQuery.section || "participants",
        form: formRef,
        node: canonicalRef,
      };
      delete nextQuery.observation;
      delete nextQuery.proposal;
      return {
        name: "study",
        params: { studyCode },
        query: nextQuery,
      };
    }

    if (parsed.kind === "proposal") {
      if (!studyCode) return null;
      const nextQuery: Record<string, string> = {
        ...baseQuery,
        proposal: canonicalRef,
        node: canonicalRef,
      };
      delete nextQuery.observation;
      return {
        name: "study",
        params: { studyCode },
        query: nextQuery,
      };
    }

    if (parsed.kind === "form_query") {
      if (!studyCode) return null;
      return {
        name: "study",
        params: { studyCode },
        query: {
          ...baseQuery,
          stage: "data",
          section: "queries",
          node: canonicalRef,
        },
      };
    }

    if (
      parsed.kind === "validation_finding" ||
      parsed.kind === "validation_run"
    ) {
      if (!studyCode) return null;
      return {
        name: "study",
        params: { studyCode },
        query: {
          ...baseQuery,
          stage: "data",
          section: "validation",
          node: canonicalRef,
        },
      };
    }

    if (studyCode) {
      return {
        name: "study",
        params: { studyCode },
        query: {
          ...baseQuery,
          node: canonicalRef,
        },
      };
    }
    return {
      name: "study",
      query: { node: canonicalRef },
    };
  }

  const normalizedType = String(payload.entityType ?? "")
    .trim()
    .toLowerCase()
    .replace(/[\s-]+/g, "_");
  const entityId = String(payload.entityId ?? "").trim();
  if (!entityId) return null;

  if (normalizedType === "study") {
    return {
      name: "study",
      params: { studyCode: entityId },
    };
  }

  if (normalizedType === "site" || normalizedType === "study_site") {
    const targetStudy = studyCode || undefined;
    return {
      name: "study",
      params: targetStudy ? { studyCode: targetStudy } : {},
      query: {
        ...baseQuery,
        stage: "conduct",
        section: "sites",
        site: entityId,
      },
    };
  }

  if (
    normalizedType === "subject" ||
    normalizedType === "participant" ||
    normalizedType === "enrollment"
  ) {
    const targetStudy = studyCode || undefined;
    return {
      name: "study",
      params: targetStudy ? { studyCode: targetStudy } : {},
      query: {
        ...baseQuery,
        stage: "conduct",
        section: "participants",
        subject: entityId,
      },
    };
  }

  if (normalizedType === "crf_form" || normalizedType === "ecrf_query") {
    if (studyCode) {
      return {
        name: "study",
        params: { studyCode },
        query: {
          ...baseQuery,
          stage: normalizedType === "ecrf_query" ? "data" : "conduct",
          section: normalizedType === "ecrf_query" ? "queries" : "participants",
          q: entityId,
        },
      };
    }
    return { name: "ecrf", query: { formId: entityId } };
  }

  if (normalizedType === "sae_case") {
    const query: Record<string, string> = { caseId: entityId };
    if (studyCode) query.studyId = studyCode;
    if (baseQuery.site) query.siteId = baseQuery.site;
    if (baseQuery.participant) query.subjectId = baseQuery.participant;
    return { name: "safety", query };
  }

  if (normalizedType === "mvr_visit") {
    if (studyCode) {
      return {
        name: "study",
        params: { studyCode },
        query: {
          ...baseQuery,
          stage: "conduct",
          section: "monitoring-visits",
        },
      };
    }
    return { name: "ctms", query: { visitId: entityId } };
  }

  if (normalizedType === "capa_action") {
    const query: Record<string, string> = { capaId: entityId };
    if (studyCode) query.studyId = studyCode;
    if (baseQuery.site) query.siteId = baseQuery.site;
    return { name: "quality", query };
  }

  if (normalizedType === "supply_kit") {
    if (studyCode) {
      return {
        name: "study",
        params: { studyCode },
        query: {
          ...baseQuery,
          stage: "conduct",
          section: "supply",
        },
      };
    }
    return null;
  }

  if (normalizedType === "tmf_document" || normalizedType === "artifact") {
    const query: Record<string, string> = { documentId: entityId };
    if (studyCode) query.studyId = studyCode;
    if (baseQuery.site) query.siteId = baseQuery.site;
    return { name: "etmf", query };
  }

  return null;
}

export type CanvasRouteSnapshot = {
  name?: unknown;
  params?: Record<string, unknown>;
  query?: Record<string, unknown>;
};

export function createCanvasEntityResolver(
  getRouter: () => Router | null,
  getRoute: () => CanvasRouteSnapshot | null,
  getFallback: () => EntityScopeFallback | null = () => null
) {
  function resolveHref(location: RouteLocationRaw | null): string | null {
    if (!location) return null;
    const router = getRouter();
    if (!router || typeof router.resolve !== "function") return null;
    try {
      return router.resolve(location).href || null;
    } catch {
      return null;
    }
  }

  async function navigateToPayload(
    payload: EntityTargetPayload
  ): Promise<void> {
    const router = getRouter();
    if (!router) return;
    const location = buildEntityRouteLocation(
      payload,
      getRoute(),
      getFallback()
    );
    if (!location) return;
    await flushRegisteredDraftSources();
    await router.push(location);
  }

  return {
    resolve: (input: {
      nodeRef?: string | null;
      entityType?: string | null;
      entityId?: string | null;
      label?: string | null;
      fallbackLabel?: string | null;
    }) => {
      const { nodeRef, entityType, entityId, label } = input;
      const route = getRoute();
      const fallback = getFallback();
      const effectiveFallbackLabel = input.fallbackLabel ?? label ?? null;
      if (nodeRef && parseNodeRef(nodeRef)) {
        ensureNodesResolved([nodeRef]);
        const summary = getNodeDisplay(nodeRef, effectiveFallbackLabel);
        const location = summary.masked
          ? null
          : buildEntityRouteLocation(
              { nodeRef: summary.ref, entityType, entityId },
              route,
              fallback
            );
        const href = resolveHref(location);
        return {
          nodeRef: summary.ref,
          label: summary.label,
          code: summary.code,
          kindLabel: summary.kindLabel,
          href,
          masked: summary.masked,
          maskReason: summary.maskReason,
          navigable: !summary.masked && Boolean(location),
        };
      }

      const location = buildEntityRouteLocation(
        { nodeRef, entityType, entityId },
        route,
        fallback
      );
      if (!location) return null;
      const href = resolveHref(location);
      return {
        label:
          effectiveFallbackLabel || (entityId ? String(entityId) : "Record"),
        href,
        navigable: true,
      };
    },
    navigate: async (payload: EntityTargetPayload) => {
      await navigateToPayload(payload);
    },
    openFullView: async (payload: EntityTargetPayload) => {
      await navigateToPayload(payload);
    },
    fetchAuditEntry: async (auditRef: string) => {
      if (typeof kernelApi.getAuditEntry !== "function") return null;
      return await kernelApi.getAuditEntry(auditRef);
    },
  };
}

export function useProvideCanvasEntityResolver(
  scopeFallback?: () => EntityScopeFallback | null
): void {
  const instance = getCurrentInstance();
  const getRouter = (): Router | null =>
    (
      instance?.appContext.config.globalProperties as {
        $router?: Router;
      }
    )?.$router ?? null;
  const getRoute = (): CanvasRouteSnapshot | null =>
    (
      instance?.appContext.config.globalProperties as {
        $route?: CanvasRouteSnapshot;
      }
    )?.$route ?? null;
  const getFallback = (): EntityScopeFallback | null =>
    typeof scopeFallback === "function" ? scopeFallback() : null;

  provideEntityLinkResolver(
    createCanvasEntityResolver(getRouter, getRoute, getFallback)
  );
}
