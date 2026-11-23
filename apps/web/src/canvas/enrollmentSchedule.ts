import { kernelApi } from "../api/kernelApi";
import {
  registerLogoutCleanup,
  registerSessionCacheClearHandler,
} from "../auth/sessionCoordinator.js";
import { mapScheduleOfActivities } from "./kernelSchedule";
import type { EnrollmentScheduleOptions } from "./kernelStudy";

export interface EnrollmentScheduleStoreTarget {
  studyCode: string;
  enrollmentSchedulesById: Record<string, EnrollmentScheduleOptions | null>;
}

let enrollmentScheduleEpoch = 0;
let enrollmentScheduleRequest = 0;
const inFlightEnrollmentSchedules = new Map<
  string,
  { request: number; promise: Promise<EnrollmentScheduleOptions | null> }
>();

function clearEnrollmentScheduleInFlight(): void {
  enrollmentScheduleEpoch += 1;
  inFlightEnrollmentSchedules.clear();
}

registerSessionCacheClearHandler(clearEnrollmentScheduleInFlight);
registerLogoutCleanup(clearEnrollmentScheduleInFlight);

function normalizeEnrollmentRef(enrollmentIdOrRef: string): string | null {
  const trimmed = enrollmentIdOrRef.trim();
  if (!trimmed) return null;
  const atIdx = trimmed.indexOf("@");
  const unversioned = atIdx < 0 ? trimmed : trimmed.slice(0, atIdx);
  return unversioned.startsWith("enrollment:")
    ? unversioned
    : `enrollment:${unversioned}`;
}

/**
 * Resolves a participant enrollment's governing protocol version, Schedule of Activities, and pending re-consent
 * lock state from Kernel's bitemporal applicability ledger (K-TIME-1, K-TIME-6, PRD-SUB-007).
 */
export async function findEnrollmentScheduleOptions(
  enrollmentIdOrRef: string,
  validAt?: string
): Promise<EnrollmentScheduleOptions | null> {
  if (typeof kernelApi.getEnrollmentScheduleOfActivities !== "function") {
    return null;
  }
  const enrollmentRef = normalizeEnrollmentRef(enrollmentIdOrRef);
  if (!enrollmentRef) return null;
  const res = await kernelApi.getEnrollmentScheduleOfActivities(
    enrollmentRef,
    validAt
  );
  if (!res) return null;
  const govNumber =
    res.governing_version?.number !== null &&
    res.governing_version?.number !== undefined
      ? Number(res.governing_version.number)
      : null;
  const pendingTransitions = Array.isArray(res.pending_transitions)
    ? res.pending_transitions.map((t) => ({
        targetVersionRef: t.target_version_ref,
        targetVersionNumber:
          t.target_version_number !== null &&
          t.target_version_number !== undefined
            ? Number(t.target_version_number)
            : null,
        amendmentIntent: t.amendment_intent ?? null,
        sourceEffectivityRef: t.source_effectivity_ref,
        requiredSince: t.required_since,
      }))
    : [];

  return {
    enrollmentRef: res.enrollment_ref,
    governingVersionRef: res.governing_version?.ref ?? null,
    governingVersionNumber: Number.isFinite(govNumber) ? govNumber : null,
    schedule: res.schedule_of_activities
      ? mapScheduleOfActivities(res.schedule_of_activities)
      : null,
    requiresReconsent: Boolean(res.requires_reconsent),
    isInconsistent: Boolean(res.is_inconsistent),
    inconsistencyReason: res.inconsistency_reason ?? null,
    pendingTransitions,
  };
}

/** Loads and caches a participant enrollment's governing protocol schedule and re-consent lock state on the active Canvas store. */
export async function loadEnrollmentScheduleForStore(
  store: EnrollmentScheduleStoreTarget,
  enrollmentId: string,
  { force = false }: { force?: boolean } = {}
): Promise<EnrollmentScheduleOptions | null> {
  const target = enrollmentId.trim();
  if (!target) return null;
  if (
    !force &&
    Object.prototype.hasOwnProperty.call(store.enrollmentSchedulesById, target)
  ) {
    return store.enrollmentSchedulesById[target];
  }
  const studyAtStart = store.studyCode;
  const key = `${studyAtStart}::${target}`;
  const existing = inFlightEnrollmentSchedules.get(key);
  if (!force && existing) {
    return existing.promise;
  }
  const epoch = enrollmentScheduleEpoch;
  const request = ++enrollmentScheduleRequest;
  const promise = (async () => {
    try {
      const loaded = await findEnrollmentScheduleOptions(target);
      if (
        epoch === enrollmentScheduleEpoch &&
        store.studyCode === studyAtStart
      ) {
        store.enrollmentSchedulesById[target] = loaded;
      }
      return loaded;
    } catch {
      if (
        epoch === enrollmentScheduleEpoch &&
        store.studyCode === studyAtStart
      ) {
        store.enrollmentSchedulesById[target] = null;
      }
      return null;
    } finally {
      if (inFlightEnrollmentSchedules.get(key)?.request === request) {
        inFlightEnrollmentSchedules.delete(key);
      }
    }
  })();
  inFlightEnrollmentSchedules.set(key, { request, promise });
  return promise;
}
