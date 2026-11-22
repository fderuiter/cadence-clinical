import { getActivePinia, type Pinia } from "pinia";
import { kernelApi } from "../api/kernelApi";
import { registerSessionCacheClearHandler } from "../auth/sessionCoordinator.js";
import { parseNodeRef, type NodeRef } from "./nodeRef";

interface AuthLikeState {
  isAuthenticated?: boolean;
  currentPersona?: string | null;
  user?: {
    id?: string | null;
    username?: string | null;
    tenantId?: string | null;
  } | null;
}

interface SessionCacheBucket {
  epoch: number;
  activeSessionKey: string;
  studyRefs: Map<string, string>;
  studyInFlight: Map<string, Promise<string | null>>;
  studySites: Map<string, StudySiteOption[]>;
  studySitesInFlight: Map<string, Promise<StudySiteOption[]>>;
  enrollments: Map<string, NodeRef>;
  enrollmentInFlight: Map<string, Promise<NodeRef | null>>;
}

function createSessionCacheBucket(): SessionCacheBucket {
  return {
    epoch: 0,
    activeSessionKey: "",
    studyRefs: new Map(),
    studyInFlight: new Map(),
    studySites: new Map(),
    studySitesInFlight: new Map(),
    enrollments: new Map(),
    enrollmentInFlight: new Map(),
  };
}

const piniaBuckets = new WeakMap<Pinia, SessionCacheBucket>();
const standaloneBucket = createSessionCacheBucket();

function getBucket(): SessionCacheBucket {
  const pinia = getActivePinia();
  if (!pinia) return standaloneBucket;
  let bucket = piniaBuckets.get(pinia);
  if (!bucket) {
    bucket = createSessionCacheBucket();
    piniaBuckets.set(pinia, bucket);
  }
  return bucket;
}

function clearBucket(bucket: SessionCacheBucket): void {
  bucket.epoch += 1;
  bucket.activeSessionKey = "";
  bucket.studyRefs.clear();
  bucket.studyInFlight.clear();
  bucket.studySites.clear();
  bucket.studySitesInFlight.clear();
  bucket.enrollments.clear();
  bucket.enrollmentInFlight.clear();
}

/**
 * Active caller cache scope (`tenant:actor:persona`), taken from the active Pinia auth state when present so positive
 * study, site, and enrollment lookups never cross tenant, account, or persona boundaries.
 */
export function getKernelStudyCallerScopeKey(): string {
  const pinia = getActivePinia();
  const auth = (pinia?.state.value as { auth?: AuthLikeState } | undefined)
    ?.auth;
  if (!auth || !auth.isAuthenticated) {
    return "anonymous::";
  }
  const tenant = String(auth.user?.tenantId ?? "").trim();
  const actor = String(auth.user?.id || auth.user?.username || "").trim();
  const persona = String(auth.currentPersona ?? "").trim();
  return `${encodeURIComponent(tenant)}:${encodeURIComponent(actor)}:${encodeURIComponent(persona)}`;
}

function normalizeStudyCode(code: string): string {
  return code.trim().toUpperCase();
}

function sessionKeyForStudy(normalizedStudy: string): string {
  if (!normalizedStudy) return "";
  return `${getKernelStudyCallerScopeKey()}::${encodeURIComponent(normalizedStudy)}`;
}

function studyKey(scope: string, normalizedStudy: string): string {
  return `${scope}::study::${encodeURIComponent(normalizedStudy)}`;
}

function sitesKey(scope: string, normalizedStudy: string): string {
  return `${scope}::sites::${encodeURIComponent(normalizedStudy)}`;
}

function enrollmentKey(
  scope: string,
  normalizedStudy: string,
  siteCode: string | null,
  subjectCode: string
): string {
  return `${scope}::enrollment::${encodeURIComponent(normalizedStudy)}::${encodeURIComponent(siteCode ?? "*")}::${encodeURIComponent(subjectCode)}`;
}

/** Clears all cached positive study references, study site options, and enrollment references immediately. */
export function clearKernelStudyCache(): void {
  clearBucket(standaloneBucket);
  const pinia = getActivePinia();
  if (pinia) {
    const bucket = piniaBuckets.get(pinia);
    if (bucket) clearBucket(bucket);
  }
}

registerSessionCacheClearHandler(() => {
  clearKernelStudyCache();
});

/** Returns the active `(tenant, actor, persona, studyCode)` session key for the current Pinia bucket, if any. */
export function getActiveKernelStudySessionKey(): string {
  return getBucket().activeSessionKey;
}

/**
 * Resets cached study, site, and enrollment lookups when switching studies or caller scopes, while preserving warm
 * entries when called again for the already-active `(tenant, actor, persona, studyCode)` session.
 */
export function resetKernelStudySession(studyCode = ""): void {
  const normalized = normalizeStudyCode(studyCode);
  const nextSessionKey = sessionKeyForStudy(normalized);
  const bucket = getBucket();
  if (nextSessionKey && nextSessionKey === bucket.activeSessionKey) {
    return;
  }
  clearKernelStudyCache();
  if (nextSessionKey) {
    getBucket().activeSessionKey = nextSessionKey;
  }
}

/**
 * The kernel reference of the study with exactly this code, found with kernel search (ADR-2484) and cached for the
 * active `(tenant, actor, persona, studyCode)` session once positively resolved, or null when the kernel has no such
 * study the reader may see.
 */
export function findKernelStudyRef(code: string): Promise<string | null> {
  const trimmed = code.trim();
  if (!trimmed) return Promise.resolve(null);

  const bucket = getBucket();
  const scope = getKernelStudyCallerScopeKey();
  const normalizedStudy = normalizeStudyCode(trimmed);
  if (!bucket.activeSessionKey) {
    bucket.activeSessionKey = sessionKeyForStudy(normalizedStudy);
  }
  const key = studyKey(scope, normalizedStudy);

  const cached = bucket.studyRefs.get(key);
  if (cached) return Promise.resolve(cached);

  const pending = bucket.studyInFlight.get(key);
  if (pending) return pending;

  const epochAtStart = bucket.epoch;
  let lookup: Promise<string | null> | undefined;
  lookup = (async () => {
    try {
      const hits = await kernelApi.search({
        text: trimmed,
        kinds: ["study"],
        limit: 5,
      });
      const resolved = hits.find((hit) => hit.exact_code)?.ref ?? null;
      if (
        resolved &&
        bucket.epoch === epochAtStart &&
        getKernelStudyCallerScopeKey() === scope
      ) {
        bucket.studyRefs.set(key, resolved);
      }
      return resolved;
    } finally {
      if (bucket.studyInFlight.get(key) === lookup) {
        bucket.studyInFlight.delete(key);
      }
    }
  })();
  bucket.studyInFlight.set(key, lookup);
  return lookup;
}

/**
 * The kernel enrollment of a participant, resolved in one read-only lookup over the kernel's unique study and subject
 * indexes (`GET /api/v1/kernel/enrollments/lookup`), or null when the study, site, or enrollment is absent,
 * mismatched, or masked for the caller (ADR-2471). Positive hits warm both the enrollment and study reference caches
 * for the active caller session; `404` responses and transient errors are not cached so newly screened participants
 * resolve on demand.
 */
export function findKernelEnrollmentRef(
  studyCode: string,
  siteCode: string | null,
  subjectCode: string
): Promise<NodeRef | null> {
  const trimmedStudy = studyCode.trim();
  const trimmedSubject = subjectCode.trim();
  const trimmedSite = siteCode?.trim() ? siteCode.trim() : null;
  if (!trimmedStudy || !trimmedSubject) return Promise.resolve(null);
  if (typeof kernelApi.lookupEnrollment !== "function") {
    return Promise.resolve(null);
  }

  const bucket = getBucket();
  const scope = getKernelStudyCallerScopeKey();
  const normalizedStudy = normalizeStudyCode(trimmedStudy);
  if (!bucket.activeSessionKey) {
    bucket.activeSessionKey = sessionKeyForStudy(normalizedStudy);
  }
  const key = enrollmentKey(
    scope,
    normalizedStudy,
    trimmedSite,
    trimmedSubject
  );

  const cached = bucket.enrollments.get(key);
  if (cached) return Promise.resolve({ ...cached });

  const pending = bucket.enrollmentInFlight.get(key);
  if (pending) return pending;

  const epochAtStart = bucket.epoch;
  let lookup: Promise<NodeRef | null> | undefined;
  lookup = (async (): Promise<NodeRef | null> => {
    try {
      const found = await kernelApi.lookupEnrollment({
        studyCode: trimmedStudy,
        subjectCode: trimmedSubject,
        siteCode: trimmedSite,
      });
      const parsed = parseNodeRef(found.enrollment_ref);
      const resolved = parsed?.kind === "enrollment" ? parsed : null;
      if (
        resolved &&
        bucket.epoch === epochAtStart &&
        getKernelStudyCallerScopeKey() === scope
      ) {
        bucket.enrollments.set(key, resolved);
        bucket.enrollments.set(
          enrollmentKey(scope, normalizedStudy, null, trimmedSubject),
          resolved
        );
        if (found.site_code?.trim()) {
          bucket.enrollments.set(
            enrollmentKey(
              scope,
              normalizedStudy,
              found.site_code.trim(),
              trimmedSubject
            ),
            resolved
          );
        }
        if (found.study_ref?.trim()) {
          bucket.studyRefs.set(
            studyKey(scope, normalizedStudy),
            found.study_ref.trim()
          );
          if (found.study_code?.trim()) {
            bucket.studyRefs.set(
              studyKey(scope, normalizeStudyCode(found.study_code)),
              found.study_ref.trim()
            );
          }
        }
      }
      return resolved ? { ...resolved } : null;
    } catch (err) {
      if ((err as { status?: number | null } | null)?.status === 404) {
        return null;
      }
      throw err;
    } finally {
      if (bucket.enrollmentInFlight.get(key) === lookup) {
        bucket.enrollmentInFlight.delete(key);
      }
    }
  })();
  bucket.enrollmentInFlight.set(key, lookup);
  return lookup;
}

export interface StudySiteOption {
  code: string;
  label: string;
}

/**
 * Visible study sites for `studyCode` from the kernel's zoom children (`study_site`), ordered by site code and cached
 * for the active `(tenant, actor, persona, studyCode)` session when positive. Returns an empty list when the kernel has
 * no visible study or study sites for the caller.
 */
export function findStudySites(studyCode: string): Promise<StudySiteOption[]> {
  const trimmed = studyCode.trim();
  if (!trimmed || typeof kernelApi.getChildren !== "function") {
    return Promise.resolve([]);
  }

  const bucket = getBucket();
  const scope = getKernelStudyCallerScopeKey();
  const normalizedStudy = normalizeStudyCode(trimmed);
  if (!bucket.activeSessionKey) {
    bucket.activeSessionKey = sessionKeyForStudy(normalizedStudy);
  }
  const key = sitesKey(scope, normalizedStudy);

  const cached = bucket.studySites.get(key);
  if (cached) {
    return Promise.resolve(cached.map((item) => ({ ...item })));
  }

  const pending = bucket.studySitesInFlight.get(key);
  if (pending) return pending;

  const epochAtStart = bucket.epoch;
  let lookup: Promise<StudySiteOption[]> | undefined;
  lookup = (async (): Promise<StudySiteOption[]> => {
    try {
      const studyRef = await findKernelStudyRef(trimmed);
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
      const options = Array.from(byCode.values()).sort((a, b) =>
        a.code.localeCompare(b.code)
      );
      if (
        options.length > 0 &&
        bucket.epoch === epochAtStart &&
        getKernelStudyCallerScopeKey() === scope
      ) {
        bucket.studySites.set(key, options);
      }
      return options.map((item) => ({ ...item }));
    } finally {
      if (bucket.studySitesInFlight.get(key) === lookup) {
        bucket.studySitesInFlight.delete(key);
      }
    }
  })();
  bucket.studySitesInFlight.set(key, lookup);
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
