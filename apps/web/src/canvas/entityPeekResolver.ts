export interface EntityPeekResolution {
  nodeRef: string;
  kind: string;
  title: string;
  summary: string;
  fullViewLocation: { path: string; query?: Record<string, string> } | null;
}

export function resolveEntityPeek(
  nodeRef: string,
  currentQuery: Record<string, unknown> = {}
): EntityPeekResolution {
  const raw = (nodeRef || "").trim();
  const preservedQuery: Record<string, string> = {};
  for (const key of [
    "studyId",
    "study_id",
    "study",
    "siteId",
    "site_id",
    "site",
    "subjectId",
    "subject_id",
    "subject",
    "participant",
    "participantId",
    "visitId",
    "visit_id",
  ]) {
    const val = currentQuery[key];
    if (typeof val === "string" && val.length > 0) {
      preservedQuery[key] = val;
    }
  }
  const m = /^([a-z0-9_]+):([^@]+)(?:@(\d+))?$/i.exec(raw);
  const kind = m ? m[1] : "entity";
  const id = m ? m[2] : raw;
  const version = m && m[3] ? Number(m[3]) : null;
  const shortId = /^[0-9a-f-]{32,36}$/i.test(id) ? id.slice(0, 8) : id;
  return {
    nodeRef: raw,
    kind,
    title: `${kind.replace(/_/g, " ")} ${shortId}${version ? ` v${version}` : ""}`,
    summary:
      "Referenced Clinical Kernel evidence record inspected without leaving your active workspace scope.",
    fullViewLocation: {
      path: "/studies",
      query: { ...preservedQuery, peekRef: raw },
    },
  };
}
