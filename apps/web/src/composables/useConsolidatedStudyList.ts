import { onMounted, ref } from "vue";
import { kernelApi, type KernelStudySummary } from "../api/kernelApi";
import { useStudyCanvasStore } from "../stores/studyCanvas";

export function useConsolidatedStudyList() {
  const canvasStore = useStudyCanvasStore();
  const studies = ref<KernelStudySummary[]>([]);
  const studiesLoading = ref(false);
  const studiesError = ref<string | null>(null);
  const selectedStudyCode = ref(canvasStore.studyCode || "");

  onMounted(async () => {
    if (typeof kernelApi.listStudies !== "function") return;
    studiesLoading.value = true;
    studiesError.value = null;
    try {
      const res = await kernelApi.listStudies();
      studies.value = res ?? [];
      if (!selectedStudyCode.value && studies.value.length > 0) {
        selectedStudyCode.value = studies.value[0].code;
      }
    } catch (err) {
      studiesError.value = err instanceof Error ? err.message : String(err);
    } finally {
      studiesLoading.value = false;
    }
  });

  return {
    studies,
    studiesLoading,
    studiesError,
    selectedStudyCode,
  };
}
