import { computed, ref, watch } from "vue";
import type { RouteLocationRaw } from "vue-router";
import {
  ecrfFormsApi,
  toFormSummaryCount,
  type EcrfFormSummary,
} from "../../api/ecrfFormsApi";
import type {
  StudyScheduleOptions,
  StudyScheduleRow,
} from "../../canvas/kernelStudy";
import { seedNodeSummary } from "../../canvas/nodeResolver";
import { useStudyCanvasStore } from "../../stores/studyCanvas";
import { isFormSummaryComplete } from "./casebookBadges";

export interface ParticipantFormsProps {
  studyCode?: string;
  siteCode?: string | null;
  subjectCode: string;
  enrollmentId: string | null;
  enrollmentLoading?: boolean;
  schedule?: StudyScheduleOptions | null;
  formLocation: (
    formId: string,
    itemId?: string | null
  ) => RouteLocationRaw | null;
  newFormLocation?: RouteLocationRaw | null;
  newFormCellLocation?: (
    collectionSpecId: string,
    plannedActivityId?: string | null
  ) => RouteLocationRaw;
  handoffLocation?: RouteLocationRaw | null;
}

export interface NumberedUnscheduledForm {
  summary: EcrfFormSummary;
  ordinal: number;
  totalForSpec: number;
}

export interface CasebookRollups {
  totalScheduledCells: number;
  scheduledCompleteCells: number;
  incompleteFormsCount: number;
  totalOpenQueries: number;
  totalErrorFindings: number;
  verifiedFormsCount: number;
  signedFormsCount: number;
  lockedFormsCount: number;
  windowDeviationFormsCount: number;
}

function formDisplayTitle(summary: EcrfFormSummary): string {
  const base = summary.collection_title || summary.collection_spec_id;
  return summary.planned_activity_id
    ? `${base} · ${summary.planned_activity_id}`
    : base;
}

export function useParticipantCasebook(
  props: ParticipantFormsProps,
  onFormsLoaded: (forms: EcrfFormSummary[]) => void
) {
  const canvas = useStudyCanvasStore();
  const forms = ref<EcrfFormSummary[]>([]);
  const enrollmentSchedule = ref<StudyScheduleOptions | null>(null);
  const loading = ref(false);
  const loadError = ref("");
  let requestSeq = 0;

  const activeSchedule = computed<StudyScheduleOptions | null>(() => {
    if (enrollmentSchedule.value) return enrollmentSchedule.value;
    if (props.schedule !== undefined) return props.schedule;
    const code = props.studyCode?.trim() ?? "";
    if (!code) return null;
    return canvas.getScheduleFor(code, props.siteCode);
  });

  const noEffectiveVersionActive = computed(() =>
    Boolean(
      props.enrollmentId &&
      activeSchedule.value?.governanceStatus === "NoEffectiveVersion"
    )
  );

  const matrixActive = computed(() => {
    const sched = activeSchedule.value;
    return Boolean(
      props.enrollmentId &&
      props.newFormCellLocation &&
      sched &&
      Array.isArray(sched.columns) &&
      Array.isArray(sched.scheduledRows) &&
      Array.isArray(sched.unscheduledRows) &&
      Array.isArray(sched.epochs) &&
      (sched.columns.length > 0 || sched.unscheduledRows.length > 0)
    );
  });

  const formsByCell = computed(() => {
    const map = new Map<string, EcrfFormSummary[]>();
    for (const summary of forms.value) {
      if (!summary.planned_activity_id) continue;
      const key = `${summary.collection_spec_id}::${summary.planned_activity_id}`;
      const bucket = map.get(key);
      if (bucket) bucket.push(summary);
      else map.set(key, [summary]);
    }
    return map;
  });

  function cellForms(specId: string, activityId: string): EcrfFormSummary[] {
    return formsByCell.value.get(`${specId}::${activityId}`) ?? [];
  }

  function isCellScheduled(row: StudyScheduleRow, colIdx: number): boolean {
    return row.columnIndices.includes(colIdx);
  }

  function cellNewLocation(
    specId: string,
    visitId: string | null
  ): RouteLocationRaw | null {
    if (props.newFormCellLocation) {
      return props.newFormCellLocation(specId, visitId);
    }
    return props.newFormLocation ?? null;
  }

  function cellClasses(row: StudyScheduleRow, colIdx: number, visitId: string) {
    const instances = cellForms(row.collectionSpecId, visitId);
    const scheduled = isCellScheduled(row, colIdx);
    if (!scheduled && instances.length === 0) {
      return ["matrix-cell", "matrix-cell--na"];
    }
    if (instances.length === 0) {
      return ["matrix-cell", "matrix-cell--not-started"];
    }
    const hasOpenQ = instances.some(
      (f) =>
        toFormSummaryCount(f.open_query_count) > 0 ||
        toFormSummaryCount(f.open_error_finding_count) > 0
    );
    const allComplete = instances.every((f) => isFormSummaryComplete(f));
    return [
      "matrix-cell",
      allComplete ? "matrix-cell--complete" : "matrix-cell--incomplete",
      hasOpenQ ? "matrix-cell--queried" : "",
    ];
  }

  const unscheduledForms = computed(() =>
    forms.value.filter((f) => !f.planned_activity_id)
  );

  const numberedUnscheduledForms = computed<NumberedUnscheduledForm[]>(() => {
    const counts = new Map<string, number>();
    for (const f of unscheduledForms.value) {
      counts.set(
        f.collection_spec_id,
        (counts.get(f.collection_spec_id) ?? 0) + 1
      );
    }
    const seen = new Map<string, number>();
    return unscheduledForms.value.map((summary) => {
      const next = (seen.get(summary.collection_spec_id) ?? 0) + 1;
      seen.set(summary.collection_spec_id, next);
      return {
        summary,
        ordinal: next,
        totalForSpec: counts.get(summary.collection_spec_id) ?? 1,
      };
    });
  });

  const unmappedScheduledForms = computed(() => {
    const sched = activeSchedule.value;
    if (
      !sched ||
      !Array.isArray(sched.scheduledRows) ||
      !Array.isArray(sched.columns)
    ) {
      return [];
    }
    const knownSpecs = new Set(
      sched.scheduledRows.map((r) => r.collectionSpecId)
    );
    const knownCols = new Set(sched.columns.map((c) => c.activityId));
    return forms.value.filter(
      (f) =>
        Boolean(f.planned_activity_id) &&
        (!knownSpecs.has(f.collection_spec_id) ||
          !knownCols.has(f.planned_activity_id as string))
    );
  });

  const rollups = computed<CasebookRollups>(() => {
    const sched = activeSchedule.value;
    const totalScheduledCells =
      sched && Array.isArray(sched.scheduledRows)
        ? sched.scheduledRows.reduce(
            (sum, row) => sum + row.columnIndices.length,
            0
          )
        : 0;

    let scheduledCompleteCells = 0;
    if (
      sched &&
      Array.isArray(sched.scheduledRows) &&
      Array.isArray(sched.columns)
    ) {
      for (const row of sched.scheduledRows) {
        for (const colIdx of row.columnIndices) {
          const col = sched.columns[colIdx];
          if (!col) continue;
          const instances = cellForms(row.collectionSpecId, col.activityId);
          if (
            instances.length > 0 &&
            instances.every((f) => isFormSummaryComplete(f))
          ) {
            scheduledCompleteCells++;
          }
        }
      }
    }

    return {
      totalScheduledCells,
      scheduledCompleteCells,
      incompleteFormsCount: forms.value.filter((f) => !isFormSummaryComplete(f))
        .length,
      totalOpenQueries: forms.value.reduce(
        (sum, f) => sum + toFormSummaryCount(f.open_query_count),
        0
      ),
      totalErrorFindings: forms.value.reduce(
        (sum, f) => sum + toFormSummaryCount(f.open_error_finding_count),
        0
      ),
      verifiedFormsCount: forms.value.filter(
        (f) =>
          toFormSummaryCount(f.verified_count) > 0 &&
          toFormSummaryCount(f.unverified_count) === 0
      ).length,
      signedFormsCount: forms.value.filter((f) => Boolean(f.signed)).length,
      lockedFormsCount: forms.value.filter((f) => Boolean(f.locked || f.frozen))
        .length,
      windowDeviationFormsCount: forms.value.filter(
        (f) => f.visit_timing === "Early" || f.visit_timing === "Late"
      ).length,
    };
  });

  async function loadFormsAndSchedule() {
    const scheduleTask =
      props.schedule === undefined &&
      props.studyCode &&
      props.newFormCellLocation
        ? canvas
            .loadScheduleOptions(
              props.studyCode,
              props.siteCode ? { siteCode: props.siteCode } : undefined
            )
            .catch(() => null)
        : Promise.resolve(null);
    const enrollmentId = props.enrollmentId;
    if (!enrollmentId) {
      forms.value = [];
      enrollmentSchedule.value = null;
      loading.value = false;
      loadError.value = "";
      onFormsLoaded([]);
      void scheduleTask;
      return;
    }
    const seq = ++requestSeq;
    const enrollmentScheduleTask =
      props.schedule === undefined &&
      typeof canvas.loadEnrollmentSchedule === "function"
        ? canvas
            .loadEnrollmentSchedule(enrollmentId)
            .then((loaded) => {
              if (seq === requestSeq) {
                enrollmentSchedule.value = loaded;
              }
              return loaded;
            })
            .catch(() => null)
        : Promise.resolve(null);
    if (typeof ecrfFormsApi?.listForEnrollment !== "function") {
      forms.value = [];
      await enrollmentScheduleTask;
      onFormsLoaded([]);
      void scheduleTask;
      return;
    }
    loading.value = true;
    loadError.value = "";
    const formsTask = (async () => {
      try {
        const result = await ecrfFormsApi.listForEnrollment(enrollmentId);
        if (seq === requestSeq) {
          forms.value = result ?? [];
          for (const summary of forms.value) {
            seedNodeSummary(`collection_instance:${summary.form_id}`, {
              kind: "collection_instance",
              title: formDisplayTitle(summary),
            });
          }
          onFormsLoaded(forms.value);
        }
      } catch (err) {
        if (seq === requestSeq) {
          forms.value = [];
          loadError.value = err instanceof Error ? err.message : String(err);
          onFormsLoaded([]);
        }
      } finally {
        if (seq === requestSeq) loading.value = false;
      }
    })();
    await Promise.all([formsTask, scheduleTask, enrollmentScheduleTask]);
  }

  watch(
    [() => props.enrollmentId, () => props.studyCode, () => props.siteCode],
    () => {
      void loadFormsAndSchedule();
    },
    { immediate: true }
  );

  return {
    forms,
    loading,
    loadError,
    activeSchedule,
    noEffectiveVersionActive,
    matrixActive,
    cellForms,
    isCellScheduled,
    cellNewLocation,
    cellClasses,
    unscheduledForms,
    numberedUnscheduledForms,
    unmappedScheduledForms,
    rollups,
    loadFormsAndSchedule,
  };
}
