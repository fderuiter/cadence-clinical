import { computed, ref, watch } from "vue";
import { findKernelStudyRef } from "../canvas/kernelStudy";
import { parseNodeRef } from "../canvas/nodeRef";

export interface StudyResolutionContext {
  code: string;
  isCurrent: () => boolean;
}

export interface UseKernelStudyRefOptions {
  /** Explicit `studyRef` prop override (e.g. `ObservationView.vue`). */
  studyRef?: () => string | null | undefined;
  /**
   * When provided and falsy while both `studyRef` and `studyCode` are empty, preserves any existing `studyRef.value`
   * (supporting standalone zoom-path resolution in `ObservationView.vue`).
   */
  embedded?: () => boolean | undefined;
  /** Synchronous cleanup callback invoked when inputs change before resolving the next study. */
  onReset?: (code: string) => void;
  /** Async or sync loader invoked once `studyRef` resolves while `studyLoading` remains active. */
  onResolved?: (
    studyRef: string,
    context: StudyResolutionContext
  ) => Promise<void> | void;
}

function errorMessageOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

/**
 * Reactive watcher that resolves a route/prop `studyCode` to its kernel `NodeRef` via `findKernelStudyRef`, tracks
 * loading, missing-study, and error states, and drops stale responses across rapid study navigation.
 */
export function useKernelStudyRef(
  studyCode: () => string | null | undefined,
  options: UseKernelStudyRefOptions = {}
) {
  const studyRef = ref<string | null>(null);
  const studyLoading = ref(false);
  const studyError = ref<string | null>(null);
  const noStudy = ref(false);

  const studyId = computed<string | null>(() => {
    const parsed = studyRef.value ? parseNodeRef(studyRef.value) : null;
    return parsed?.kind === "study" ? parsed.id : null;
  });

  let lookupSeq = 0;

  async function runForCurrentStudy(
    task: (resolvedStudyRef: string, isCurrent: () => boolean) => Promise<void>
  ): Promise<void> {
    const current = studyRef.value;
    if (!current) return;
    const seq = ++lookupSeq;
    const isCurrent = () => seq === lookupSeq;
    studyLoading.value = true;
    studyError.value = null;
    try {
      await task(current, isCurrent);
    } catch (error) {
      if (isCurrent()) {
        studyError.value = errorMessageOf(error);
      }
    } finally {
      if (isCurrent()) {
        studyLoading.value = false;
      }
    }
  }

  watch(
    [() => options.studyRef?.() ?? null, () => studyCode() ?? ""],
    async ([explicitStudyRef, rawCode]) => {
      const seq = ++lookupSeq;
      const isCurrent = () => seq === lookupSeq;
      const code = rawCode.trim();

      studyError.value = null;
      noStudy.value = false;
      options.onReset?.(code);

      const explicit = explicitStudyRef?.trim() || null;
      if (explicit) {
        studyRef.value = explicit;
        if (!options.onResolved) {
          studyLoading.value = false;
          return;
        }
        studyLoading.value = true;
        try {
          await options.onResolved(explicit, { code, isCurrent });
        } catch (error) {
          if (isCurrent()) {
            studyError.value = errorMessageOf(error);
          }
        } finally {
          if (isCurrent()) {
            studyLoading.value = false;
          }
        }
        return;
      }

      if (!code) {
        if (options.embedded === undefined || Boolean(options.embedded())) {
          studyRef.value = null;
        }
        noStudy.value = true;
        studyLoading.value = false;
        return;
      }

      studyRef.value = null;
      studyLoading.value = true;
      try {
        const resolved = await findKernelStudyRef(code);
        if (!isCurrent()) return;
        studyRef.value = resolved;
        if (!resolved) {
          noStudy.value = true;
          return;
        }
        await options.onResolved?.(resolved, { code, isCurrent });
      } catch (error) {
        if (isCurrent()) {
          studyRef.value = null;
          studyError.value = errorMessageOf(error);
        }
      } finally {
        if (isCurrent()) {
          studyLoading.value = false;
        }
      }
    },
    { immediate: true }
  );

  return {
    studyRef,
    studyId,
    studyLoading,
    studyError,
    noStudy,
    runForCurrentStudy,
  };
}
