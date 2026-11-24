import { getActivePinia, type Pinia } from "pinia";
import { kernelApi } from "../api/kernelApi";
import {
  registerLogoutCleanup,
  registerSessionCacheClearHandler,
} from "../auth/sessionCoordinator.js";
import {
  getKernelStudyCallerScopeKey,
  registerKernelStudySessionBridge,
} from "../stores/studyScopeBridge";
import { parseNodeRef, type NodeRef } from "./nodeRef";

export { getKernelStudyCallerScopeKey };

interface SessionCacheBucket {
  epoch: number;
  activeSessionKey: string;
  studyRefs: Map<string, string>;
  studyInFlight: Map<string, Promise<string | null>>;
  enrollmentStudyInFlight: Map<string, Promise<string | null>>;
  studySites: Map<string, StudySiteOption[]>;
  studySiteRefs: Map<string, Map<string, string>>;
  studySitesInFlight: Map<string, Promise<StudySiteOption[]>>;
  enrollments: Map<string, NodeRef>;
  enrollmentStudySites: Map<string, string>;
  enrollmentInFlight: Map<string, Promise<NodeRef | null>>;
  scheduleOptions: Map<string, StudyScheduleOptions>;
  scheduleOptionsInFlight: Map<string, Promise<StudyScheduleOptions | null>>;
}

function createSessionCacheBucket(): SessionCacheBucket {
  return {
    epoch: 0,
    activeSessionKey: "",
    studyRefs: new Map(),
    studyInFlight: new Map(),
    enrollmentStudyInFlight: new Map(),
    studySites: new Map(),
    studySiteRefs: new Map(),
    studySitesInFlight: new Map(),
    enrollments: new Map(),
    enrollmentStudySites: new Map(),
    enrollmentInFlight: new Map(),
    scheduleOptions: new Map(),
    scheduleOptionsInFlight: new Map(),
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
  bucket.enrollmentStudyInFlight.clear();
  bucket.studySites.clear();
  bucket.studySiteRefs.clear();
  bucket.studySitesInFlight.clear();
  bucket.enrollments.clear();
  bucket.enrollmentStudySites.clear();
  bucket.enrollmentInFlight.clear();
  bucket.scheduleOptions.clear();
  bucket.scheduleOptionsInFlight.clear();
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

function scheduleKey(scope: string, normalizedStudy: string): string {
  return `${scope}::schedule::${encodeURIComponent(normalizedStudy)}`;
}

function enrollmentKey(
  scope: string,
  normalizedStudy: string,
  siteCode: string | null,
  subjectCode: string
): string {
  return `${scope}::enrollment::${encodeURIComponent(normalizedStudy)}::${encodeURIComponent(siteCode ?? "*")}::${encodeURIComponent(subjectCode)}`;
}

function enrollmentSiteKey(scope: string, enrollmentId: string): string {
  return `${scope}::enrollment-site::${encodeURIComponent(enrollmentId)}`;
}

/** Clears all cached positive study references, study site options, enrollment references, and schedules immediately. */
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
registerLogoutCleanup(clearKernelStudyCache);

/** Returns the active `(tenant, actor, persona, studyCode)` session key for the current Pinia bucket, if any. */
export function getActiveKernelStudySessionKey(): string {
  return getBucket().activeSessionKey;
}

/**
 * Resets cached study, site, enrollment, and schedule lookups when switching studies or caller scopes, while preserving
 * warm entries when called again for the already-active `(tenant, actor, persona, studyCode)` session.
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

registerKernelStudySessionBridge({
  clearCache: clearKernelStudyCache,
  resetSession: resetKernelStudySession,
  getActiveSessionKey: getActiveKernelStudySessionKey,
});

function ensureActiveStudySession(
  bucket: SessionCacheBucket,
  normalizedStudy: string
): void {
  const nextSessionKey = sessionKeyForStudy(normalizedStudy);
  if (!nextSessionKey) return;
  if (bucket.activeSessionKey && bucket.activeSessionKey !== nextSessionKey) {
    clearBucket(bucket);
  }
  bucket.activeSessionKey = nextSessionKey;
}

/**
 * The kernel reference of the study with exactly this code, found with kernel search (ADR-2484) — or piggybacked off
 * an already in-flight enrollment lookup for the same study — and cached for the active `(tenant, actor, persona,
 * studyCode)` session once positively resolved, or null when the kernel has no such study the reader may see.
 */
export function findKernelStudyRef(code: string): Promise<string | null> {
  const trimmed = code.trim();
  if (!trimmed) return Promise.resolve(null);

  const bucket = getBucket();
  const scope = getKernelStudyCallerScopeKey();
  const normalizedStudy = normalizeStudyCode(trimmed);
  ensureActiveStudySession(bucket, normalizedStudy);
  const key = studyKey(scope, normalizedStudy);

  const cached = bucket.studyRefs.get(key);
  if (cached) return Promise.resolve(cached);

  const pending = bucket.studyInFlight.get(key);
  if (pending) return pending;

  const epochAtStart = bucket.epoch;
  let lookup: Promise<string | null> | undefined;
  lookup = (async () => {
    try {
      const inFlightEnrollmentStudy = bucket.enrollmentStudyInFlight.get(key);
      if (inFlightEnrollmentStudy) {
        const hinted = await inFlightEnrollmentStudy;
        if (hinted) {
          if (
            bucket.epoch === epochAtStart &&
            getKernelStudyCallerScopeKey() === scope
          ) {
            bucket.studyRefs.set(key, hinted);
          }
          return hinted;
        }
        const cachedAfterHint = bucket.studyRefs.get(key);
        if (cachedAfterHint) {
          return cachedAfterHint;
        }
      }

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
  ensureActiveStudySession(bucket, normalizedStudy);
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

  const sKey = studyKey(scope, normalizedStudy);
  let resolveStudyHint = (_studyRef: string | null): void => {};
  let studyHintPromise: Promise<string | null> | null = null;
  if (
    !bucket.studyRefs.has(sKey) &&
    !bucket.enrollmentStudyInFlight.has(sKey)
  ) {
    studyHintPromise = new Promise<string | null>((resolve) => {
      resolveStudyHint = resolve;
    });
    bucket.enrollmentStudyInFlight.set(sKey, studyHintPromise);
  }

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
      const resolvedStudyRef = found.study_ref?.trim() || null;
      const resolvedSiteRef = found.study_site_ref?.trim() || null;
      if (
        resolved &&
        bucket.epoch === epochAtStart &&
        getKernelStudyCallerScopeKey() === scope
      ) {
        bucket.enrollments.set(key, resolved);
        if (resolvedSiteRef) {
          const siteParsed = parseNodeRef(resolvedSiteRef);
          const unversionedSiteRef =
            siteParsed?.kind === "study_site"
              ? `${siteParsed.kind}:${siteParsed.id}`
              : resolvedSiteRef;
          bucket.enrollmentStudySites.set(
            enrollmentSiteKey(scope, resolved.id),
            unversionedSiteRef
          );
        }
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
        if (resolvedStudyRef) {
          bucket.studyRefs.set(sKey, resolvedStudyRef);
          if (found.study_code?.trim()) {
            bucket.studyRefs.set(
              studyKey(scope, normalizeStudyCode(found.study_code)),
              resolvedStudyRef
            );
          }
        }
      }
      resolveStudyHint(resolved ? resolvedStudyRef : null);
      return resolved ? { ...resolved } : null;
    } catch (err) {
      resolveStudyHint(null);
      if ((err as { status?: number | null } | null)?.status === 404) {
        return null;
      }
      throw err;
    } finally {
      resolveStudyHint(null);
      if (
        studyHintPromise &&
        bucket.enrollmentStudyInFlight.get(sKey) === studyHintPromise
      ) {
        bucket.enrollmentStudyInFlight.delete(sKey);
      }
      if (bucket.enrollmentInFlight.get(key) === lookup) {
        bucket.enrollmentInFlight.delete(key);
      }
    }
  })();
  bucket.enrollmentInFlight.set(key, lookup);
  return lookup;
}

/**
 * Unversioned `study_site:<id>` reference cached for `enrollmentId` in the active caller scope from a prior
 * `findKernelEnrollmentRef` lookup, or null when not yet cached.
 */
export function getCachedEnrollmentStudySiteRef(
  enrollmentId: string
): string | null {
  const trimmed = enrollmentId.trim();
  if (!trimmed) return null;
  const bucket = getBucket();
  const scope = getKernelStudyCallerScopeKey();
  return (
    bucket.enrollmentStudySites.get(enrollmentSiteKey(scope, trimmed)) ?? null
  );
}

export interface StudySiteOption {
  code: string;
  label: string;
}

function invalidateBucketStudySites(
  bucket: SessionCacheBucket,
  normalizedStudy?: string
): void {
  if (!normalizedStudy) {
    bucket.studySites.clear();
    bucket.studySiteRefs.clear();
    bucket.studySitesInFlight.clear();
    return;
  }
  const suffix = `::sites::${encodeURIComponent(normalizedStudy)}`;
  for (const existingKey of Array.from(bucket.studySites.keys())) {
    if (existingKey.endsWith(suffix)) {
      bucket.studySites.delete(existingKey);
    }
  }
  for (const existingKey of Array.from(bucket.studySiteRefs.keys())) {
    if (existingKey.endsWith(suffix)) {
      bucket.studySiteRefs.delete(existingKey);
    }
  }
  for (const existingKey of Array.from(bucket.studySitesInFlight.keys())) {
    if (existingKey.endsWith(suffix)) {
      bucket.studySitesInFlight.delete(existingKey);
    }
  }
}

/**
 * Invalidates cached study-site lookups (for one study or all studies) after linking a site to a study.
 */
export function invalidateStudySitesCache(studyCode?: string): void {
  const normalized = studyCode ? normalizeStudyCode(studyCode) : "";
  invalidateBucketStudySites(standaloneBucket, normalized || undefined);
  const pinia = getActivePinia();
  if (pinia) {
    const bucket = piniaBuckets.get(pinia);
    if (bucket) {
      invalidateBucketStudySites(bucket, normalized || undefined);
    }
  }
}

/**
 * Visible study sites for `studyCode` from the kernel's zoom children (`study_site`) or study-site directory, ordered
 * by site code and cached for the active `(tenant, actor, persona, studyCode)` session when positive. Returns an empty
 * list when the kernel has no visible study or study sites for the caller.
 */
export function findStudySites(studyCode: string): Promise<StudySiteOption[]> {
  const trimmed = studyCode.trim();
  if (
    !trimmed ||
    (typeof kernelApi.getChildren !== "function" &&
      typeof kernelApi.listStudySites !== "function")
  ) {
    return Promise.resolve([]);
  }

  const bucket = getBucket();
  const scope = getKernelStudyCallerScopeKey();
  const normalizedStudy = normalizeStudyCode(trimmed);
  ensureActiveStudySession(bucket, normalizedStudy);
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
      const byCode = new Map<string, StudySiteOption>();
      const refsByCode = new Map<string, string>();
      const recordStudySiteRef = (
        rawCode: string,
        rawRef: string | null | undefined
      ): void => {
        const lowerCode = rawCode.toLowerCase();
        const parsed = rawRef ? parseNodeRef(rawRef) : null;
        const canonical =
          parsed?.kind === "study_site" ? `${parsed.kind}:${parsed.id}` : "";
        if (refsByCode.has(lowerCode)) {
          refsByCode.set(lowerCode, "");
          return;
        }
        if (canonical) {
          refsByCode.set(lowerCode, canonical);
        }
      };
      let studyRef: string | null = null;
      if (typeof kernelApi.getChildren === "function") {
        try {
          studyRef = await findKernelStudyRef(trimmed);
          if (studyRef) {
            const children = await kernelApi.getChildren(studyRef);
            if (Array.isArray(children)) {
              for (const child of children) {
                if (child.node.kind !== "study_site" || child.node.mask) {
                  continue;
                }
                const code = child.node.code?.trim();
                if (!code) continue;
                recordStudySiteRef(code, child.node.ref);
                if (byCode.has(code)) continue;
                const title = child.node.title?.trim();
                const label =
                  title && title !== code ? `${code} — ${title}` : code;
                byCode.set(code, { code, label });
              }
            }
          }
        } catch {
          // If zoom-children or search is unavailable/unmocked while listStudySites is available,
          // rethrow only when listStudySites is not available.
          if (typeof kernelApi.listStudySites !== "function") {
            throw new Error("Study site lookup failed");
          }
        }
      }
      if (byCode.size === 0 && typeof kernelApi.listStudySites === "function") {
        try {
          const linked = await kernelApi.listStudySites(studyRef ?? trimmed);
          if (Array.isArray(linked)) {
            for (const site of linked) {
              if (site.mask) continue;
              const code = site.site_code?.trim();
              if (!code) continue;
              recordStudySiteRef(code, site.ref);
              if (byCode.has(code)) continue;
              const name = site.site_name?.trim();
              const label = name && name !== code ? `${code} — ${name}` : code;
              byCode.set(code, { code, label });
            }
          }
        } catch {
          // Study may not exist in kernel yet; return empty list.
        }
      }
      const options = Array.from(byCode.values()).sort((a, b) =>
        a.code.localeCompare(b.code)
      );
      if (
        options.length > 0 &&
        bucket.epoch === epochAtStart &&
        bucket.studySitesInFlight.get(key) === lookup &&
        getKernelStudyCallerScopeKey() === scope
      ) {
        bucket.studySites.set(key, options);
        bucket.studySiteRefs.set(key, refsByCode);
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

/**
 * Resolves the canonical `study_site:<uuid>` `NodeRef` for `(studyCode, siteCode)` from the kernel's
 * study-site directory (or visible `study_site` zoom children fallback), or `null` when the study or
 * site is not visible to the caller.
 */
export async function findKernelStudySiteRef(
  studyCode: string,
  siteCode: string
): Promise<string | null> {
  const normalizedStudy = studyCode.trim();
  const normalizedSite = siteCode.trim().toLowerCase();
  if (!normalizedStudy || !normalizedSite) {
    return null;
  }
  const bucket = getBucket();
  const scope = getKernelStudyCallerScopeKey();
  const studyCacheCode = normalizeStudyCode(normalizedStudy);
  ensureActiveStudySession(bucket, studyCacheCode);
  const key = sitesKey(scope, studyCacheCode);
  const pendingSites = bucket.studySitesInFlight.get(key);
  if (pendingSites) {
    try {
      await pendingSites;
    } catch {
      // Fall through to direct lookup when concurrent study-site preload fails.
    }
  }
  const cachedRefs = bucket.studySiteRefs.get(key);
  if (cachedRefs) {
    return cachedRefs.get(normalizedSite) || null;
  }
  if (typeof kernelApi.listStudySites === "function") {
    try {
      const linked = await kernelApi.listStudySites(normalizedStudy);
      if (Array.isArray(linked) && linked.length > 0) {
        const matches = linked.filter(
          (item) =>
            Boolean(item.ref) &&
            item.site_code?.trim().toLowerCase() === normalizedSite
        );
        if (matches.length === 1) {
          const parsed = parseNodeRef(matches[0].ref);
          return parsed?.kind === "study_site"
            ? `${parsed.kind}:${parsed.id}`
            : null;
        }
        return null;
      }
    } catch {
      // Fall back to zoom-children lookup when study-sites endpoint is unavailable or mocked out.
    }
  }
  if (
    typeof kernelApi.search !== "function" ||
    typeof kernelApi.getChildren !== "function"
  ) {
    return null;
  }
  const studyRef = await findKernelStudyRef(normalizedStudy);
  if (!studyRef) return null;
  const children = await kernelApi.getChildren(studyRef);
  const matches = children.filter(
    (child) =>
      child.node.kind === "study_site" &&
      !child.node.mask &&
      Boolean(child.node.ref) &&
      child.node.code?.trim().toLowerCase() === normalizedSite
  );
  if (matches.length !== 1) return null;
  const parsed = parseNodeRef(matches[0].node.ref);
  return parsed?.kind === "study_site" ? `${parsed.kind}:${parsed.id}` : null;
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
export function findStudyScheduleOptions(
  studyCode: string
): Promise<StudyScheduleOptions | null> {
  if (
    typeof kernelApi.listProtocolVersions !== "function" ||
    typeof kernelApi.getScheduleOfActivities !== "function"
  ) {
    return Promise.resolve(null);
  }
  const trimmed = studyCode.trim();
  if (!trimmed) return Promise.resolve(null);

  const bucket = getBucket();
  const scope = getKernelStudyCallerScopeKey();
  const normalizedStudy = normalizeStudyCode(trimmed);
  ensureActiveStudySession(bucket, normalizedStudy);
  const key = scheduleKey(scope, normalizedStudy);

  const cached = bucket.scheduleOptions.get(key);
  if (cached) return Promise.resolve(cached);

  const pending = bucket.scheduleOptionsInFlight.get(key);
  if (pending) return pending;

  const epochAtStart = bucket.epoch;
  let lookup: Promise<StudyScheduleOptions | null> | undefined;
  lookup = (async () => {
    try {
      const studyRef = await findKernelStudyRef(trimmed);
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
      if (
        bucket.epoch === epochAtStart &&
        getKernelStudyCallerScopeKey() === scope
      ) {
        bucket.scheduleOptions.set(key, resolved);
      }
      return resolved;
    } finally {
      if (bucket.scheduleOptionsInFlight.get(key) === lookup) {
        bucket.scheduleOptionsInFlight.delete(key);
      }
    }
  })();
  bucket.scheduleOptionsInFlight.set(key, lookup);
  return lookup;
}

export interface CallerSiteScopeAuthStore {
  refresh: (minValidity: number) => Promise<unknown>;
  siteIds?: string[];
  assignedSites?: string[];
}

export async function syncCallerSiteScope(
  authStore: CallerSiteScopeAuthStore,
  siteCode?: string | null
): Promise<void> {
  if (!siteCode) return;
  try {
    await authStore.refresh(-1);
  } catch {
    // Non-OIDC test sessions continue with the existing in-memory session.
  }
  if (
    Array.isArray(authStore.siteIds) &&
    !authStore.siteIds.some(
      (code: string) => code.toUpperCase() === siteCode.toUpperCase()
    )
  ) {
    authStore.siteIds = [...authStore.siteIds, siteCode];
  }
  if (
    Array.isArray(authStore.assignedSites) &&
    !authStore.assignedSites.some(
      (code: string) => code.toUpperCase() === siteCode.toUpperCase()
    )
  ) {
    authStore.assignedSites = [...authStore.assignedSites, siteCode];
  }
}
