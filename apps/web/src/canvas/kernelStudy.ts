import { registerLogoutCleanup } from "../auth/sessionCoordinator.js";
import { kernelApi } from "../api/kernelApi";
import { parseNodeRef, type NodeRef } from "./nodeRef";

let activeStudySessionKey = "";
let sessionEpoch = 0;

const studyRefCache = new Map<string, string>();
const inFlightStudyRef = new Map<string, Promise<string | null>>();

const enrollmentRefCache = new Map<string, NodeRef>();
const inFlightEnrollmentRef = new Map<string, Promise<NodeRef | null>>();

const studySitesCache = new Map<string, StudySiteOption[]>();
const inFlightStudySites = new Map<string, Promise<StudySiteOption[]>>();

const scheduleOptionsCache = new Map<string, StudyScheduleOptions>();
const inFlightScheduleOptions = new Map<
  string,
  Promise<StudyScheduleOptions | null>
>();

/** Clears all session-cached study, site, enrollment, and schedule lookups (e.g. on logout or study switch). */
export function clearKernelStudyCache(): void {
  activeStudySessionKey = "";
  sessionEpoch += 1;
  studyRefCache.clear();
  inFlightStudyRef.clear();
  enrollmentRefCache.clear();
  inFlightEnrollmentRef.clear();
  studySitesCache.clear();
  inFlightStudySites.clear();
  scheduleOptionsCache.clear();
  inFlightScheduleOptions.clear();
}

registerLogoutCleanup(clearKernelStudyCache);

/**
 * Resets cached study/site/enrollment/schedule lookups when switching to a different study session.
 * Calling with the already-active study code preserves the warm session cache.
 */
export function resetKernelStudySession(studyCode = ""): void {
  const normalized = studyCode.trim();
  if (normalized && normalized === activeStudySessionKey) return;
  clearKernelStudyCache();
  activeStudySessionKey = normalized;
}

/**
 * The kernel reference of the study with exactly this code, found with kernel search (ADR-2484), or null when the
 * kernel has no such study the reader may see. The Canvas addresses a study by its code in the URL; kernel reads take
 * its NodeRef. Positive lookups are cached for the active study session; null and 403/error responses are never cached.
 */
export function findKernelStudyRef(code: string): Promise<string | null> {
  const key = code.trim();
  if (!key) return Promise.resolve(null);
  const cached = studyRefCache.get(key);
  if (cached) return Promise.resolve(cached);
  const pending = inFlightStudyRef.get(key);
  if (pending) return pending;
  const epoch = sessionEpoch;
  let lookup: Promise<string | null> | undefined;
  lookup = (async () => {
    try {
      const hits = await kernelApi.search({
        text: key,
        kinds: ["study"],
        limit: 5,
      });
      const found = hits.find((hit) => hit.exact_code)?.ref ?? null;
      if (found && epoch === sessionEpoch) {
        studyRefCache.set(key, found);
      }
      return found;
    } finally {
      if (inFlightStudyRef.get(key) === lookup) {
        inFlightStudyRef.delete(key);
      }
    }
  })();
  inFlightStudyRef.set(key, lookup);
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
  const normalizedStudy = studyCode.trim();
  const normalizedSite = siteCode?.trim() || "";
  const normalizedSubject = subjectCode.trim();
  if (!normalizedStudy || !normalizedSubject) return null;

  const cacheKey = `${normalizedStudy}\0${normalizedSite}\0${normalizedSubject}`;
  const cached = enrollmentRefCache.get(cacheKey);
  if (cached) return cached;
  const pending = inFlightEnrollmentRef.get(cacheKey);
  if (pending) return pending;

  const epoch = sessionEpoch;
  let lookup: Promise<NodeRef | null> | undefined;
  lookup = (async () => {
    try {
      const studyRef = await findKernelStudyRef(normalizedStudy);
      if (!studyRef) return null;
      const studySites = (await kernelApi.getChildren(studyRef)).filter(
        (child) =>
          child.node.kind === "study_site" &&
          child.node.ref &&
          (!normalizedSite || child.node.code === normalizedSite)
      );
      const enrollments = (
        await Promise.all(
          studySites.map((site) =>
            kernelApi.getChildren(site.node.ref as string)
          )
        )
      )
        .flat()
        .filter(
          (child) =>
            child.node.kind === "enrollment" &&
            child.node.code === normalizedSubject
        );
      if (enrollments.length !== 1) return null;
      const ref = parseNodeRef(enrollments[0].node.ref);
      const resolved = ref?.kind === "enrollment" ? ref : null;
      if (resolved && epoch === sessionEpoch) {
        enrollmentRefCache.set(cacheKey, resolved);
      }
      return resolved;
    } finally {
      if (inFlightEnrollmentRef.get(cacheKey) === lookup) {
        inFlightEnrollmentRef.delete(cacheKey);
      }
    }
  })();
  inFlightEnrollmentRef.set(cacheKey, lookup);
  return lookup;
}

export interface StudySiteOption {
  code: string;
  label: string;
}

/**
 * Invalidates cached study-site lookups (for one study or all studies) after linking a site to a study.
 */
export function invalidateStudySitesCache(studyCode?: string): void {
  const key = studyCode?.trim();
  if (key) {
    studySitesCache.delete(key);
    inFlightStudySites.delete(key);
    return;
  }
  studySitesCache.clear();
  inFlightStudySites.clear();
}

/**
 * Visible study sites for `studyCode` from the kernel's zoom children (`study_site`) or study-site directory,
 * ordered by site code. Returns an empty list when the kernel has no visible study or study sites for the caller.
 */
export async function findStudySites(
  studyCode: string
): Promise<StudySiteOption[]> {
  if (
    typeof kernelApi.getChildren !== "function" &&
    typeof kernelApi.listStudySites !== "function"
  ) {
    return [];
  }
  const key = studyCode.trim();
  if (!key) return [];
  const cached = studySitesCache.get(key);
  if (cached) return cached;
  const pending = inFlightStudySites.get(key);
  if (pending) return pending;

  const epoch = sessionEpoch;
  let lookup: Promise<StudySiteOption[]> | undefined;
  lookup = (async () => {
    try {
      const byCode = new Map<string, StudySiteOption>();
      let studyRef: string | null = null;
      if (typeof kernelApi.getChildren === "function") {
        studyRef = await findKernelStudyRef(key);
        if (studyRef) {
          const children = await kernelApi.getChildren(studyRef);
          for (const child of children) {
            if (child.node.kind !== "study_site" || child.node.mask) continue;
            const code = child.node.code?.trim();
            if (!code || byCode.has(code)) continue;
            const title = child.node.title?.trim();
            const label = title && title !== code ? `${code} — ${title}` : code;
            byCode.set(code, { code, label });
          }
        }
      }
      if (byCode.size === 0 && typeof kernelApi.listStudySites === "function") {
        try {
          const linked = await kernelApi.listStudySites(studyRef ?? key);
          for (const site of linked) {
            const code = site.site_code?.trim();
            if (!code || byCode.has(code)) continue;
            const name = site.site_name?.trim();
            const label = name && name !== code ? `${code} — ${name}` : code;
            byCode.set(code, { code, label });
          }
        } catch {
          // Study may not exist in kernel yet; return empty list.
        }
      }
      const options = Array.from(byCode.values()).sort((a, b) =>
        a.code.localeCompare(b.code)
      );
      if (options.length > 0 && epoch === sessionEpoch) {
        studySitesCache.set(key, options);
      }
      return options;
    } finally {
      if (inFlightStudySites.get(key) === lookup) {
        inFlightStudySites.delete(key);
      }
    }
  })();
  inFlightStudySites.set(key, lookup);
  return lookup;
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
  const key = studyCode.trim();
  if (!key) return null;
  const cached = scheduleOptionsCache.get(key);
  if (cached) return cached;
  const pending = inFlightScheduleOptions.get(key);
  if (pending) return pending;

  const epoch = sessionEpoch;
  let lookup: Promise<StudyScheduleOptions | null> | undefined;
  lookup = (async () => {
    try {
      const studyRef = await findKernelStudyRef(key);
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

      const resolved: StudyScheduleOptions = {
        versionRef: soa.version,
        title: soa.title ?? "",
        epochs,
        columns,
        scheduledRows: mappedRows.filter((row) => row.columnIndices.length > 0),
        unscheduledRows: mappedRows.filter(
          (row) => row.columnIndices.length === 0
        ),
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
      if (epoch === sessionEpoch) {
        scheduleOptionsCache.set(key, resolved);
      }
      return resolved;
    } finally {
      if (inFlightScheduleOptions.get(key) === lookup) {
        inFlightScheduleOptions.delete(key);
      }
    }
  })();
  inFlightScheduleOptions.set(key, lookup);
  return lookup;
}
