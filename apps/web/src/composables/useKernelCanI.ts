import { ref } from "vue";
import { kernelApi, type KernelDecision } from "../api/kernelApi";
import { findKernelStudySiteRef } from "../canvas/kernelStudy";

/** Capability code from `EffectivityCommands.cs` (`ActivateStudySiteCommand`). */
export const STUDY_SITE_ACTIVATE_CAPABILITY = "study_site.activate";

/**
 * Dry-run authorization probe wrapping `kernelApi.canI` (`GET /api/v1/kernel/authorization/can-i`).
 * Preserves the kernel's `KernelDecision` and `KernelExplanation` verbatim (`K-EXP-1`) without
 * fabricating client-side authorization rules when the kernel endpoint is unavailable.
 */
export function useKernelCanI() {
  const decision = ref<KernelDecision | null>(null);
  const resourceRef = ref<string | null>(null);
  const loading = ref(false);
  const error = ref<string | null>(null);
  let requestSeq = 0;

  function reset(): void {
    requestSeq++;
    decision.value = null;
    resourceRef.value = null;
    loading.value = false;
    error.value = null;
  }

  async function probe(
    capability: string,
    resource: string | null | undefined
  ): Promise<KernelDecision | null> {
    const seq = ++requestSeq;
    const trimmedResource = resource?.trim() ?? "";
    decision.value = null;
    error.value = null;
    resourceRef.value = trimmedResource || null;

    if (!trimmedResource || typeof kernelApi.canI !== "function") {
      loading.value = false;
      return null;
    }

    loading.value = true;
    try {
      const result = await kernelApi.canI(capability, trimmedResource);
      if (seq !== requestSeq) return null;
      decision.value = result;
      return result;
    } catch (err) {
      if (seq === requestSeq) {
        error.value = err instanceof Error ? err.message : String(err);
      }
      return null;
    } finally {
      if (seq === requestSeq) {
        loading.value = false;
      }
    }
  }

  async function probeStudySiteActivate(
    studyCode: string,
    siteCode: string
  ): Promise<KernelDecision | null> {
    const seq = ++requestSeq;
    decision.value = null;
    error.value = null;
    resourceRef.value = null;

    if (typeof kernelApi.canI !== "function") {
      loading.value = false;
      return null;
    }

    loading.value = true;
    try {
      const resolvedRef = await findKernelStudySiteRef(studyCode, siteCode);
      if (seq !== requestSeq) return null;
      resourceRef.value = resolvedRef;
      if (!resolvedRef) {
        return null;
      }
      const result = await kernelApi.canI(
        STUDY_SITE_ACTIVATE_CAPABILITY,
        resolvedRef
      );
      if (seq !== requestSeq) return null;
      decision.value = result;
      return result;
    } catch (err) {
      if (seq === requestSeq) {
        error.value = err instanceof Error ? err.message : String(err);
      }
      return null;
    } finally {
      if (seq === requestSeq) {
        loading.value = false;
      }
    }
  }

  return {
    decision,
    resourceRef,
    loading,
    error,
    reset,
    probe,
    probeStudySiteActivate,
  };
}
