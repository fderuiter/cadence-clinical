import {
  kernelApi,
  type KernelLock,
  type KernelStudyLocksQuery,
} from "../api/kernelApi";

export interface DirectScopeLockItem {
  lockRef: string;
  kind: string;
}

export interface CoveringScopeLockItem {
  kind: string;
  scope: string;
}

export interface HeldScopeLock {
  lock_id: unknown;
  kind: string;
  scope: unknown;
}

export type ScopeLockSource =
  | HeldScopeLock
  | {
      ref?: string | null;
      kind?: string | null;
      scope_ref?: string | null;
      masked?: boolean;
      released?: boolean | null;
    };

export interface StudyLocksResult {
  locks: KernelLock[];
  hidden: {
    more_exist: boolean;
    reasons: string[];
  };
}

/** Normalizes a lock identifier so it carries the canonical `assertion:` prefix. */
export function toAssertionLockRef(lockId: unknown): string {
  const idText = String(lockId ?? "").trim();
  if (!idText) return "";
  return idText.startsWith("assertion:") ? idText : `assertion:${idText}`;
}

/**
 * Reads study locks and collapsed hidden-scope indicator from the kernel when a study reference and
 * `kernelApi.listStudyLocks` are available.
 */
export async function fetchStudyLocksResult(
  studyRef: string | null | undefined,
  query?: KernelStudyLocksQuery
): Promise<StudyLocksResult> {
  if (!studyRef || typeof kernelApi.listStudyLocks !== "function") {
    return { locks: [], hidden: { more_exist: false, reasons: [] } };
  }
  const res = await kernelApi.listStudyLocks(studyRef, query);
  return {
    locks: res?.locks ?? [],
    hidden: res?.hidden ?? { more_exist: false, reasons: [] },
  };
}

/**
 * Reads study locks from the kernel when a study reference and `kernelApi.listStudyLocks` are available,
 * returning an empty list when either is absent.
 */
export async function fetchStudyLocks(
  studyRef: string | null | undefined,
  query?: KernelStudyLocksQuery
): Promise<KernelLock[]> {
  const result = await fetchStudyLocksResult(studyRef, query);
  return result.locks;
}

/**
 * Extracts active, unmasked direct locks targeting `targetScope` and normalizes each `lockRef` with the
 * canonical `assertion:` prefix.
 */
export function toDirectScopeLocks(
  locks: ReadonlyArray<ScopeLockSource> | null | undefined,
  targetScope: string
): DirectScopeLockItem[] {
  if (!locks || !targetScope) return [];
  const items: DirectScopeLockItem[] = [];
  for (const entry of locks) {
    if ("lock_id" in entry) {
      if (String(entry.scope) !== targetScope) continue;
      const lockRef = toAssertionLockRef(entry.lock_id);
      if (lockRef) {
        items.push({ lockRef, kind: entry.kind });
      }
    } else {
      if (
        entry.masked ||
        entry.released ||
        !entry.ref ||
        !entry.kind ||
        entry.scope_ref !== targetScope
      ) {
        continue;
      }
      const lockRef = toAssertionLockRef(entry.ref);
      if (lockRef) {
        items.push({ lockRef, kind: entry.kind });
      }
    }
  }
  return items;
}

/**
 * Extracts active, unmasked ancestor/covering locks whose scope differs from `targetScope`.
 */
export function toCoveringScopeLocks(
  locks: ReadonlyArray<ScopeLockSource> | null | undefined,
  targetScope: string
): CoveringScopeLockItem[] {
  if (!locks || !targetScope) return [];
  const items: CoveringScopeLockItem[] = [];
  for (const entry of locks) {
    if ("lock_id" in entry) {
      const scope = String(entry.scope ?? "").trim();
      if (!scope || scope === targetScope) continue;
      items.push({ kind: entry.kind, scope });
    } else {
      if (
        entry.masked ||
        entry.released ||
        !entry.kind ||
        !entry.scope_ref ||
        entry.scope_ref === targetScope
      ) {
        continue;
      }
      items.push({ kind: entry.kind, scope: entry.scope_ref });
    }
  }
  return items;
}
