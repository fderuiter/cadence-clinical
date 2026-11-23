import {
  kernelApi,
  type KernelLock,
  type KernelStudyLocksQuery,
} from "../api/kernelApi";

export interface DirectScopeLockItem {
  lockRef: string;
  kind: string;
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
      kind: string;
      scope_ref?: string | null;
      masked?: boolean;
      released?: boolean;
    };

/** Normalizes a lock identifier so it carries the canonical `assertion:` prefix. */
export function toAssertionLockRef(lockId: unknown): string {
  const idText = String(lockId ?? "").trim();
  if (!idText) return "";
  return idText.startsWith("assertion:") ? idText : `assertion:${idText}`;
}

/**
 * Reads study locks from the kernel when a study reference and `kernelApi.listStudyLocks` are available,
 * returning an empty list when either is absent.
 */
export async function fetchStudyLocks(
  studyRef: string | null | undefined,
  query?: KernelStudyLocksQuery
): Promise<KernelLock[]> {
  if (!studyRef || typeof kernelApi.listStudyLocks !== "function") {
    return [];
  }
  const res = await kernelApi.listStudyLocks(studyRef, query);
  return res?.locks ?? [];
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
