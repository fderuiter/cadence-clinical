import type { RouteLocationRaw } from "vue-router";
import {
  buildScopedStudyLocation,
  type WorkspaceScopeSnapshot,
} from "./palette";
import {
  collectionInstanceRef,
  formatNodeRef,
  observationNodeRef,
} from "./nodeRef";
import { parseLookupNodeRef } from "./nodeResolver";

export interface EntityDestinationContext {
  studyCode?: string | null;
  site?: string | null;
  participant?: string | null;
  entityCode?: string | null;
  scope?: Partial<WorkspaceScopeSnapshot> | null;
}

/**
 * Resolves the canonical Clinical Canvas full-view route for a `NodeRef` while preserving
 * active study, site, and participant scope (ADR-2651, ADR-2594).
 */
export function resolveEntityFullViewLocation(
  nodeRefText: string | null | undefined,
  context: EntityDestinationContext = {}
): RouteLocationRaw | null {
  const parsed = parseLookupNodeRef(nodeRefText);
  if (!parsed) return null;

  const canonical = formatNodeRef(parsed);
  const studyCode = (context.studyCode ?? "").trim();
  const site = (context.site ?? "").trim();
  const participant = (context.participant ?? "").trim();
  const entityCode = (context.entityCode ?? "").trim();
  const baseScope = context.scope ?? null;

  const effectiveScope: Partial<WorkspaceScopeSnapshot> | undefined =
    baseScope || site || participant
      ? {
          ...baseScope,
          ...(site ? { siteId: site, explicitSiteId: site } : {}),
          ...(participant
            ? {
                participantRef: participant.includes(":") ? participant : "",
                subjectCode: !participant.includes(":") ? participant : "",
              }
            : {}),
        }
      : undefined;

  if (parsed.kind === "study") {
    const targetStudy = entityCode || studyCode;
    if (!targetStudy) return null;
    return buildScopedStudyLocation(targetStudy, {}, effectiveScope);
  }

  if (!studyCode) return null;

  switch (parsed.kind) {
    case "enrollment":
    case "participant":
      return buildScopedStudyLocation(
        studyCode,
        {
          stage: "conduct",
          participant: canonical,
          node: "",
        },
        effectiveScope
      );
    case "protocol_version":
      return buildScopedStudyLocation(
        studyCode,
        {
          stage: "design",
          section: "protocol-versions",
          node: canonical,
        },
        effectiveScope
      );
    case "collection_instance": {
      const formRef = collectionInstanceRef(canonical) ?? canonical;
      return buildScopedStudyLocation(
        studyCode,
        {
          stage: "conduct",
          form: formRef,
          ...(participant ? { participant } : {}),
        },
        effectiveScope
      );
    }
    case "observation": {
      const obsRef = observationNodeRef(canonical) ?? canonical;
      return buildScopedStudyLocation(
        studyCode,
        {
          stage: "conduct",
          observation: obsRef,
          ...(participant ? { participant } : {}),
        },
        effectiveScope
      );
    }
    case "proposal":
      return buildScopedStudyLocation(
        studyCode,
        {
          proposal: canonical,
        },
        effectiveScope
      );
    case "validation_finding":
      return buildScopedStudyLocation(
        studyCode,
        {
          stage: "data",
          section: "validation",
          node: canonical,
        },
        effectiveScope
      );
    case "assertion":
    case "form_query":
      return buildScopedStudyLocation(
        studyCode,
        {
          stage: "data",
          section: "queries",
          node: canonical,
        },
        effectiveScope
      );
    case "study_site":
      return buildScopedStudyLocation(
        studyCode,
        {
          stage: "conduct",
          section: "sites",
          node: canonical,
          ...(entityCode ? { site: entityCode } : {}),
        },
        effectiveScope
      );
    case "artifact_version":
      return buildScopedStudyLocation(
        studyCode,
        {
          stage: "start",
          section: "documents",
          node: canonical,
        },
        effectiveScope
      );
    default:
      return buildScopedStudyLocation(
        studyCode,
        {
          node: canonical,
        },
        effectiveScope
      );
  }
}
