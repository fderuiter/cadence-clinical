import { effectScope, nextTick, ref } from "vue";
import { flushPromises } from "@vue/test-utils";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { findKernelStudyRef } from "../../src/canvas/kernelStudy";
import { useKernelStudyRef } from "../../src/composables/useKernelStudyRef";

vi.mock("../../src/canvas/kernelStudy", () => ({
  findKernelStudyRef: vi.fn(),
}));

const STUDY_UUID_A = "01960000-0000-7000-8000-000000000001";
const STUDY_UUID_B = "01960000-0000-7000-8000-000000000002";
const STUDY_REF_A = `study:${STUDY_UUID_A}@1`;
const STUDY_REF_B = `study:${STUDY_UUID_B}`;

describe("useKernelStudyRef", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("resolves studyCode, computes studyId, tracks loading/noStudy/error states, and runs onResolved", async () => {
    vi.mocked(findKernelStudyRef).mockResolvedValueOnce(STUDY_REF_A);
    const resolvedCalls: string[] = [];
    const code = ref("CAD-101");

    const scope = effectScope();
    const state = scope.run(() =>
      useKernelStudyRef(() => code.value, {
        onResolved: async (studyRef) => {
          resolvedCalls.push(studyRef);
        },
      })
    )!;

    expect(state.studyLoading.value).toBe(true);
    await flushPromises();

    expect(findKernelStudyRef).toHaveBeenCalledWith("CAD-101");
    expect(state.studyRef.value).toBe(STUDY_REF_A);
    expect(state.studyId.value).toBe(STUDY_UUID_A);
    expect(state.studyLoading.value).toBe(false);
    expect(state.noStudy.value).toBe(false);
    expect(state.studyError.value).toBeNull();
    expect(resolvedCalls).toEqual([STUDY_REF_A]);

    // Missing study
    vi.mocked(findKernelStudyRef).mockResolvedValueOnce(null);
    code.value = "CAD-404";
    await nextTick();
    await flushPromises();

    expect(state.studyRef.value).toBeNull();
    expect(state.studyId.value).toBeNull();
    expect(state.noStudy.value).toBe(true);
    expect(state.studyError.value).toBeNull();

    // Error state
    vi.mocked(findKernelStudyRef).mockRejectedValueOnce(
      new Error("Kernel unavailable")
    );
    code.value = "CAD-500";
    await nextTick();
    await flushPromises();

    expect(state.studyRef.value).toBeNull();
    expect(state.noStudy.value).toBe(false);
    expect(state.studyError.value).toBe("Kernel unavailable");
    expect(state.studyLoading.value).toBe(false);

    // Retry via reload()
    vi.mocked(findKernelStudyRef).mockResolvedValueOnce(STUDY_REF_B);
    await state.reload();
    expect(state.studyRef.value).toBe(STUDY_REF_B);
    expect(state.studyError.value).toBeNull();

    scope.stop();
  });

  it("drops stale lookups and stale runForCurrentStudy tasks on rapid navigation", async () => {
    let resolveFirst!: (val: string | null) => void;
    vi.mocked(findKernelStudyRef)
      .mockImplementationOnce(
        () =>
          new Promise<string | null>((resolve) => {
            resolveFirst = resolve;
          })
      )
      .mockResolvedValueOnce(STUDY_REF_B);

    const code = ref("CAD-A");
    const loadedPayloads: string[] = [];

    const scope = effectScope();
    const state = scope.run(() =>
      useKernelStudyRef(() => code.value, {
        onReset: () => {
          loadedPayloads.length = 0;
        },
        onResolved: async (resolved, { isCurrent }) => {
          if (isCurrent()) {
            loadedPayloads.push(resolved);
          }
        },
      })
    )!;

    // Navigate to CAD-B before CAD-A resolves
    code.value = "CAD-B";
    await nextTick();
    await flushPromises();

    expect(state.studyRef.value).toBe(STUDY_REF_B);
    expect(loadedPayloads).toEqual([STUDY_REF_B]);

    // Stale CAD-A resolves late and must be ignored
    resolveFirst(STUDY_REF_A);
    await flushPromises();

    expect(state.studyRef.value).toBe(STUDY_REF_B);
    expect(loadedPayloads).toEqual([STUDY_REF_B]);

    // Overlapping runForCurrentStudy calls keep only the latest
    let finishFirstTask!: () => void;
    const firstTask = state.runForCurrentStudy(async (_, isCurrent) => {
      await new Promise<void>((resolve) => {
        finishFirstTask = resolve;
      });
      if (isCurrent()) loadedPayloads.push("stale-page");
    });
    const secondTask = state.runForCurrentStudy(async (_, isCurrent) => {
      if (isCurrent()) loadedPayloads.push("fresh-page");
    });

    await secondTask;
    finishFirstTask();
    await firstTask;

    expect(loadedPayloads).toEqual([STUDY_REF_B, "fresh-page"]);
    scope.stop();
  });

  it("supports explicit studyRef and embedded overrides for ObservationView", async () => {
    const explicitStudyRef = ref<string | null>(STUDY_REF_A);
    const code = ref<string | null>(null);
    const embedded = ref<boolean | undefined>(false);

    const scope = effectScope();
    const state = scope.run(() =>
      useKernelStudyRef(() => code.value, {
        studyRef: () => explicitStudyRef.value,
        embedded: () => embedded.value,
      })
    )!;
    await flushPromises();

    expect(findKernelStudyRef).not.toHaveBeenCalled();
    expect(state.studyRef.value).toBe(STUDY_REF_A);

    // Standalone (!embedded) with neither studyRef nor studyCode preserves zoom-path studyRef
    state.studyRef.value = STUDY_REF_B;
    explicitStudyRef.value = null;
    await nextTick();
    await flushPromises();
    expect(state.studyRef.value).toBe(STUDY_REF_B);

    // Embedded mode clears studyRef when studyCode becomes empty
    embedded.value = true;
    code.value = "   ";
    await nextTick();
    await flushPromises();
    expect(state.studyRef.value).toBeNull();

    scope.stop();
  });
});
