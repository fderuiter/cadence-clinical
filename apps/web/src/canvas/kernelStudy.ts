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
      return hits.find((hit) => hit.exact_code)?.ref ?? null;
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
  label: string;
}

/**
 * Visible study sites for `studyCode` from the kernel's study-site directory (or zoom children fallback), ordered by site code.
 * Returns an empty list when the kernel has no visible study or study sites for the caller.
 */
export async function findStudySites(
  studyCode: string
): Promise<StudySiteOption[]> {
  if (typeof kernelApi.listStudySites === "function") {
    try {
      const linked = await kernelApi.listStudySites(studyCode);
      if (Array.isArray(linked) && linked.length > 0) {
        const byCode = new Map<string, StudySiteOption>();
        for (const item of linked) {
          const code = item.site_code?.trim();
          if (!code || byCode.has(code)) continue;
          const name = item.site_name?.trim();
          const country = item.country?.trim();
          const detail =
            name && name !== code
              ? country
                ? `${name} (${country})`
                : name
              : country || "";
          byCode.set(code, {
            code,
            label: detail ? `${code} — ${detail}` : code,
          });
        }
        return Array.from(byCode.values()).sort((a, b) =>
          a.code.localeCompare(b.code)
        );
      }
    } catch {
      // Fall back to zoom-children lookup when study-sites endpoint is unavailable or mocked out.
    }
  }
  if (typeof kernelApi.getChildren !== "function") return [];
  const studyRef = await findKernelStudyRef(studyCode);
  if (!studyRef) return [];
  const children = await kernelApi.getChildren(studyRef);
  const byCode = new Map<string, StudySiteOption>();
  for (const child of children) {
    if (child.node.kind !== "study_site" || child.node.mask) continue;
    const code = child.node.code?.trim();
    if (!code || byCode.has(code)) continue;
    const title = child.node.title?.trim();
    const label = title && title !== code ? `${code} — ${title}` : code;
    byCode.set(code, { code, label });
  }
  return Array.from(byCode.values()).sort((a, b) =>
    a.code.localeCompare(b.code)
  );
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

export interface StudyScheduleEpoch {
  epochId: string;
  name: string;
  order: number;
  firstColumn: number;
  columnCount: number;
}

export interface StudyScheduleColumn {
  activityId: string;
  name: string;
  epochId: string;
  studyDay: number | null;
  windowBefore: number;
  windowAfter: number;
  locations: string[];
}

export interface StudyScheduleRow {
  collectionSpecId: string;
  name: string;
  itemCount: number;
  columnIndices: number[];
  plannedActivityIds: string[];
}

export interface StudyScheduleOptions {
  versionRef: string;
  title: string;
  epochs: StudyScheduleEpoch[];
  columns: StudyScheduleColumn[];
  scheduledRows: StudyScheduleRow[];
  unscheduledRows: StudyScheduleRow[];
  collectionSpecs: StudyCollectionSpecOption[];
  plannedActivities: StudyPlannedActivityOption[];
}

function buildScheduleEpochs(
  rawEpochs: ReadonlyArray<{
    epoch_id?: string;
    name?: string;
    order?: number | string;
    first_column?: number | string;
    column_count?: number | string;
  }>,
  columns: ReadonlyArray<StudyScheduleColumn>
): StudyScheduleEpoch[] {
  if (rawEpochs.length > 0) {
    return rawEpochs
      .map((epoch, index) => ({
        epochId: epoch.epoch_id ?? `epoch-${index}`,
        name: epoch.name ?? epoch.epoch_id ?? `Epoch ${index + 1}`,
        order: Number(epoch.order) || index,
        firstColumn: Number(epoch.first_column) || 0,
        columnCount: Number(epoch.column_count) || 0,
      }))
      .filter((epoch) => epoch.columnCount > 0);
  }
  const synthesized: StudyScheduleEpoch[] = [];
  for (let i = 0; i < columns.length; i++) {
    const epochId = columns[i].epochId || "study";
    const last = synthesized.at(-1);
    if (last && last.epochId === epochId) {
      last.columnCount += 1;
    } else {
      synthesized.push({
        epochId,
        name: epochId,
        order: synthesized.length + 1,
        firstColumn: i,
        columnCount: 1,
      });
    }
  }
  return synthesized;
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
  const rawColumns = Array.isArray(soa.columns) ? soa.columns : [];
  const rawRows = Array.isArray(soa.rows) ? soa.rows : [];
  const columns: StudyScheduleColumn[] = rawColumns.map((col) => ({
    activityId: col.activity_id,
    name: col.name,
    epochId: col.epoch_id ?? "",
    studyDay:
      col.study_day !== null && col.study_day !== undefined
        ? Number(col.study_day)
        : null,
    windowBefore: Number(col.window_before) || 0,
    windowAfter: Number(col.window_after) || 0,
    locations: Array.isArray(col.locations) ? col.locations : [],
  }));
  const epochs = buildScheduleEpochs(
    Array.isArray(soa.epochs) ? soa.epochs : [],
    columns
  );
  const mappedRows: StudyScheduleRow[] = rawRows.map((row) => {
    const columnIndices = (Array.isArray(row.columns) ? row.columns : [])
      .map((idx) => Number(idx))
      .filter(
        (idx) => Number.isInteger(idx) && idx >= 0 && idx < columns.length
      );
    return {
      collectionSpecId: row.collection_spec_id,
      name: row.name,
      itemCount: Array.isArray(row.item_ids) ? row.item_ids.length : 0,
      columnIndices,
      plannedActivityIds: columnIndices
        .map((idx) => columns[idx]?.activityId)
        .filter((id): id is string => Boolean(id)),
    };
  });

  return {
    versionRef: soa.version,
    title: soa.title ?? "",
    epochs,
    columns,
    scheduledRows: mappedRows.filter((row) => row.columnIndices.length > 0),
    unscheduledRows: mappedRows.filter((row) => row.columnIndices.length === 0),
    collectionSpecs: mappedRows.map((row) => ({
      id: row.collectionSpecId,
      name: row.name,
      plannedActivityIds: row.plannedActivityIds,
    })),
    plannedActivities: columns.map((col) => ({
      id: col.activityId,
      name: col.name,
      studyDay: col.studyDay,
    })),
  };
}
