import type { RouteLocationRaw } from "vue-router";
import {
  buildScopedStudyLocation,
  type WorkspaceScopeSnapshot,
} from "./palette";

export interface EntityPeekResolution {
  nodeRef: string;
  kind: string;
  title: string;
  summary: string;
  fullViewLocation: RouteLocationRaw | null;
}

export interface ResolveEntityPeekOptions {
  studyCode?: string;
  workspaceScope?: Partial<WorkspaceScopeSnapshot> | null;
}

export function resolveEntityPeek(
  nodeRef: string,
  currentQuery: Record<string, unknown> = {},
  options?: ResolveEntityPeekOptions
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

  const explicitCanvasStudy = (
    options?.studyCode ||
    options?.workspaceScope?.studyCode ||
    ""
  ).trim();
  const stage =
    typeof currentQuery.stage === "string" && currentQuery.stage.trim()
      ? currentQuery.stage.trim()
      : undefined;
  const section =
    typeof currentQuery.section === "string" && currentQuery.section.trim()
      ? currentQuery.section.trim()
      : undefined;

  const fullViewLocation: RouteLocationRaw = explicitCanvasStudy
    ? buildScopedStudyLocation(
        explicitCanvasStudy,
        {
          stage,
          section,
          node: raw,
        },
        options?.workspaceScope ?? {
          studyCode: explicitCanvasStudy,
          siteId:
            preservedQuery.site ||
            preservedQuery.siteId ||
            preservedQuery.site_id ||
            "",
          subjectCode:
            preservedQuery.subjectId ||
            preservedQuery.subject_id ||
            preservedQuery.subject ||
            "",
          participantRef:
            preservedQuery.participant || preservedQuery.participantId || "",
        }
      )
    : {
        path: "/studies",
        query: { ...preservedQuery, peekRef: raw },
      };

  return {
    nodeRef: raw,
    kind,
    title: `${kind.replace(/_/g, " ")} ${shortId}${version ? ` v${version}` : ""}`,
    summary:
      "Referenced Clinical Kernel evidence record inspected without leaving your active workspace scope.",
    fullViewLocation,
  };
}
