import { kernelApi } from "../api/kernelApi";
import { parseNodeRef, type NodeRef } from "./nodeRef";

// Lookups still in flight, so panes opening together on study home share one search; nothing is kept once it answers.
const inFlight = new Map<string, Promise<string | null>>();

/**
 * The kernel reference of the study with exactly this code, found with kernel search (ADR-2484), or null when the
 * kernel has no such study the reader may see. The Canvas addresses a study by its code in the URL; kernel reads take
 * its NodeRef.
 */
export function findKernelStudyRef(code: string): Promise<string | null> {
  const pending = inFlight.get(code);
  if (pending) return pending;
  const lookup = (async () => {
    try {
      const hits = await kernelApi.search({
        text: code,
        kinds: ["study"],
        limit: 5,
      });
      return Array.isArray(hits)
        ? (hits.find((hit) => hit.exact_code)?.ref ?? null)
        : null;
    } finally {
      inFlight.delete(code);
    }
  })();
  inFlight.set(code, lookup);
  return lookup;
}

/**
 * The kernel enrollment of a participant, found by walking the kernel's zoom children (study, then its study sites,
 * then their enrollments) and matching codes, or null when the kernel has no single such enrollment the reader may
 * see. The Canvas lists participants from Execution by subject and site code; kernel module reads, such as eCRF
 * queries, take the enrollment's id. A study site is matched by site code when the participant has one; an
 * enrollment by its subject code. Two matching enrollments return null rather than a guess.
 */
export async function findKernelEnrollmentRef(
  studyCode: string,
  siteCode: string | null,
  subjectCode: string
): Promise<NodeRef | null> {
  const studyRef = await findKernelStudyRef(studyCode);
  if (!studyRef) return null;
  const studySites = (await kernelApi.getChildren(studyRef)).filter(
    (child) =>
      child.node.kind === "study_site" &&
      child.node.ref &&
      (!siteCode || child.node.code === siteCode)
  );
  const enrollments = (
    await Promise.all(
      studySites.map((site) => kernelApi.getChildren(site.node.ref as string))
    )
  )
    .flat()
    .filter(
      (child) =>
        child.node.kind === "enrollment" && child.node.code === subjectCode
    );
  if (enrollments.length !== 1) return null;
  const ref = parseNodeRef(enrollments[0].node.ref);
  return ref?.kind === "enrollment" ? ref : null;
}

export interface StudySiteOption {
  code: string;
  title: string;
  ref: string;
}

/**
 * Visible study sites under a study in the kernel's zoom hierarchy, ordered by site code.
 */
export async function findStudySites(
  studyCode: string
): Promise<StudySiteOption[]> {
  if (
    !studyCode ||
    typeof kernelApi.search !== "function" ||
    typeof kernelApi.getChildren !== "function"
  ) {
    return [];
  }
  const studyRef = await findKernelStudyRef(studyCode);
  if (!studyRef) return [];
  const children = await kernelApi.getChildren(studyRef);
  if (!Array.isArray(children)) return [];
  const seen = new Set<string>();
  const sites: StudySiteOption[] = [];
  for (const child of children) {
    const node = child.node;
    if (
      node.kind !== "study_site" ||
      node.mask ||
      !node.code ||
      !node.ref ||
      seen.has(node.code)
    ) {
      continue;
    }
    seen.add(node.code);
    sites.push({
      code: node.code,
      title: node.title || node.code,
      ref: node.ref,
    });
  }
  return sites.sort((a, b) => a.code.localeCompare(b.code));
}

export interface StudyCollectionSpecOption {
  id: string;
  name: string;
  plannedActivityIds: string[];
}

export interface StudyPlannedActivityOption {
  id: string;
  name: string;
  studyDay: number | null;
}

export interface StudyScheduleOptions {
  versionRef: string;
  collectionSpecs: StudyCollectionSpecOption[];
  plannedActivities: StudyPlannedActivityOption[];
}

/**
 * Structured form and visit choices from the study's active (or latest visible) protocol version's Schedule of
 * Activities (ADR-2475), or null when the kernel has no visible protocol version or schedule for the study.
 */
export async function findStudyScheduleOptions(
  studyCode: string
): Promise<StudyScheduleOptions | null> {
  if (
    typeof kernelApi.listProtocolVersions !== "function" ||
    typeof kernelApi.getScheduleOfActivities !== "function"
  ) {
    return null;
  }
  const studyRef = await findKernelStudyRef(studyCode);
  if (!studyRef) return null;
  const versions = await kernelApi.listProtocolVersions(studyRef);
  const chosen =
    versions.find((v) => v.ref && !v.mask && v.status === "Approved") ??
    versions.find((v) => v.ref && !v.mask);
  if (!chosen?.ref) return null;
  const soa = await kernelApi.getScheduleOfActivities(chosen.ref);
  return {
    versionRef: soa.version,
    collectionSpecs: soa.rows.map((row) => ({
      id: row.collection_spec_id,
      name: row.name,
      plannedActivityIds: row.columns
        .map((idx) => soa.columns[Number(idx)]?.activity_id)
        .filter((id): id is string => Boolean(id)),
    })),
    plannedActivities: soa.columns.map((col) => ({
      id: col.activity_id,
      name: col.name,
      studyDay:
        col.study_day !== null && col.study_day !== undefined
          ? Number(col.study_day)
          : null,
    })),
  };
}
