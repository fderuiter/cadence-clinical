import type {
  LocationQuery,
  LocationQueryRaw,
  RouteLocationRaw,
} from "vue-router";
import {
  collectionInstanceRef,
  formatNodeRef,
  parseNodeRef,
  tryNodeRef,
} from "./nodeRef";

function firstString(value: unknown): string {
  if (typeof value === "string") return value.trim();
  if (Array.isArray(value) && typeof value[0] === "string") {
    return value[0].trim();
  }
  return "";
}

/**
 * Translates legacy `/ecrf` query parameters (`studyId`, `subjectId`, `siteId`, `collectionSpecId`, `plannedActivityId`,
 * `visitId`, `highlightItemId`, `formId`, etc.) into an addressable Clinical Canvas location (`/studies/:studyCode` or
 * `/studies`) preserving participant, site, spec, visit, item, and form state.
 */
export function buildCanvasLocationFromEcrfQuery(
  query: LocationQuery | Record<string, unknown> | null | undefined
): RouteLocationRaw {
  const studyCode =
    firstString(query?.studyId) ||
    firstString(query?.studyCode) ||
    firstString(query?.study);

  if (!studyCode) {
    return { name: "study" };
  }

  const canvasQuery: LocationQueryRaw = {
    stage: "conduct",
  };

  const site = firstString(query?.siteId) || firstString(query?.site);
  if (site) {
    canvasQuery.site = site;
  }

  const rawParticipant =
    firstString(query?.participant) ||
    firstString(query?.subjectId) ||
    firstString(query?.subject);
  if (rawParticipant) {
    const parsedParticipant =
      parseNodeRef(rawParticipant.toLowerCase()) ??
      tryNodeRef("participant", rawParticipant.toLowerCase());
    if (
      parsedParticipant?.kind === "enrollment" ||
      parsedParticipant?.kind === "participant"
    ) {
      canvasQuery.participant = formatNodeRef({
        kind: parsedParticipant.kind,
        id: parsedParticipant.id,
      });
    } else {
      canvasQuery.subject = rawParticipant;
    }
  }

  const spec = firstString(query?.collectionSpecId) || firstString(query?.spec);
  if (spec) {
    canvasQuery.spec = spec;
  }

  const visit =
    firstString(query?.plannedActivityId) ||
    firstString(query?.visitId) ||
    firstString(query?.visit);
  if (visit) {
    canvasQuery.visit = visit;
  }

  const item =
    firstString(query?.highlightItemId) ||
    firstString(query?.itemId) ||
    firstString(query?.item);
  if (item) {
    canvasQuery.item = item;
  }

  const rawForm = firstString(query?.formId) || firstString(query?.form);
  if (rawForm === "new") {
    canvasQuery.form = "new";
  } else if (rawForm) {
    canvasQuery.form = collectionInstanceRef(rawForm) ?? rawForm;
  } else if (spec || visit) {
    canvasQuery.form = "new";
  }

  if (!canvasQuery.participant && !canvasQuery.subject && !canvasQuery.form) {
    canvasQuery.section = "participants";
  }

  return {
    name: "study",
    params: { studyCode },
    query: canvasQuery,
  };
}
