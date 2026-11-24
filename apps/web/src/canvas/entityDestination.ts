import type { LocationQueryRaw, RouteLocationRaw } from "vue-router";
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

  if (parsed.kind === "study") {
    const targetStudy = entityCode || studyCode;
    if (!targetStudy) return null;
    const query: LocationQueryRaw = {};
    if (site) query.site = site;
    if (participant) query.participant = participant;
    return Object.keys(query).length > 0
      ? { name: "study", params: { studyCode: targetStudy }, query }
      : { name: "study", params: { studyCode: targetStudy } };
  }

  if (!studyCode) return null;

  switch (parsed.kind) {
    case "enrollment":
    case "participant": {
      const query: LocationQueryRaw = {
        stage: "conduct",
        participant: canonical,
      };
      if (site) query.site = site;
      return { name: "study", params: { studyCode }, query };
    }
    case "protocol_version": {
      const query: LocationQueryRaw = {
        stage: "design",
        section: "protocol-versions",
        node: canonical,
      };
      if (site) query.site = site;
      return { name: "study", params: { studyCode }, query };
    }
    case "collection_instance": {
      const formRef = collectionInstanceRef(canonical) ?? canonical;
      const query: LocationQueryRaw = {
        stage: "conduct",
        form: formRef,
      };
      if (site) query.site = site;
      if (participant) query.participant = participant;
      return { name: "study", params: { studyCode }, query };
    }
    case "observation": {
      const obsRef = observationNodeRef(canonical) ?? canonical;
      const query: LocationQueryRaw = {
        stage: "conduct",
        observation: obsRef,
      };
      if (site) query.site = site;
      if (participant) query.participant = participant;
      return { name: "study", params: { studyCode }, query };
    }
    case "proposal": {
      const query: LocationQueryRaw = {
        proposal: canonical,
      };
      if (site) query.site = site;
      return { name: "study", params: { studyCode }, query };
    }
    case "validation_finding": {
      const query: LocationQueryRaw = {
        stage: "data",
        section: "validation",
        node: canonical,
      };
      if (site) query.site = site;
      return { name: "study", params: { studyCode }, query };
    }
    case "assertion":
    case "form_query": {
      const query: LocationQueryRaw = {
        stage: "data",
        section: "queries",
        node: canonical,
      };
      if (site) query.site = site;
      return { name: "study", params: { studyCode }, query };
    }
    case "study_site": {
      const query: LocationQueryRaw = {
        stage: "conduct",
        section: "sites",
        node: canonical,
      };
      if (entityCode || site) query.site = entityCode || site;
      return { name: "study", params: { studyCode }, query };
    }
    case "artifact_version": {
      const query: LocationQueryRaw = {
        stage: "start",
        section: "documents",
        node: canonical,
      };
      if (site) query.site = site;
      return { name: "study", params: { studyCode }, query };
    }
    default: {
      const query: LocationQueryRaw = {
        node: canonical,
      };
      if (site) query.site = site;
      return { name: "study", params: { studyCode }, query };
    }
  }
}
