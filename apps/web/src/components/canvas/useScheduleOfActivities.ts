import { computed, ref, watch } from "vue";
import {
  describeKernelError,
  isMasked,
  kernelApi,
  type KernelProtocolVersion,
  type KernelScheduleOfActivities,
  type KernelSoaColumn,
  type KernelSoaRow,
} from "../../api/kernelApi";
import { parseNodeRef } from "../../canvas/nodeRef";
import {
  ensureNodesResolved,
  normalizeLookupNodeRef,
  seedNodeSummary,
} from "../../canvas/nodeResolver";
import { useKernelStudyRef } from "../../composables/useKernelStudyRef";

export interface UseScheduleOfActivitiesProps {
  readonly studyCode: string;
  readonly selectedRef: string;
}

export function useScheduleOfActivities(
  props: UseScheduleOfActivitiesProps,
  onSelectNode: (nodeRef: string) => void
) {
  const versions = ref<KernelProtocolVersion[]>([]);
  const localVersionRef = ref("");
  const soa = ref<KernelScheduleOfActivities | null>(null);
  const soaLoading = ref(false);
  const soaError = ref("");
  let soaRequest = 0;

  const visibleVersions = computed(() =>
    versions.value.filter((v) => Boolean(v.ref) && !isMasked(v))
  );

  const byVersionId = computed(
    () =>
      new Map(
        visibleVersions.value.flatMap((v) => {
          const parsed = parseNodeRef(v.ref);
          return parsed ? [[parsed.id, v] as const] : [];
        })
      )
  );

  function pickDefaultVersionRef(list: KernelProtocolVersion[]): string {
    const visible = list.filter((v) => v.ref && !isMasked(v));
    const approved = visible.find((v) => v.status === "Approved");
    return approved?.ref ?? visible[0]?.ref ?? "";
  }

  const activeVersionRef = computed(() => {
    if (
      props.selectedRef &&
      parseNodeRef(props.selectedRef)?.kind === "protocol_version"
    ) {
      return props.selectedRef;
    }
    if (localVersionRef.value) return localVersionRef.value;
    return pickDefaultVersionRef(versions.value);
  });

  const activeVersion = computed<KernelProtocolVersion | null>(() => {
    const target = activeVersionRef.value;
    if (!target) return null;
    const targetId = parseNodeRef(target)?.id;
    return (
      visibleVersions.value.find(
        (v) =>
          v.ref === target || (targetId && parseNodeRef(v.ref)?.id === targetId)
      ) ?? null
    );
  });

  const parentVersion = computed<KernelProtocolVersion | null>(() => {
    const parentRef = activeVersion.value?.parent_version;
    if (!parentRef) return null;
    const parentId = parseNodeRef(parentRef)?.id;
    return parentId ? (byVersionId.value.get(parentId) ?? null) : null;
  });

  const parentVersionNodeRef = computed(
    () =>
      parentVersion.value?.ref ??
      (parseNodeRef(activeVersion.value?.parent_version)
        ? activeVersion.value!.parent_version!
        : "")
  );

  const parentVersionLabel = computed(() =>
    parentVersion.value ? `Version ${parentVersion.value.number}` : ""
  );

  watch(
    () => props.selectedRef,
    (next) => {
      if (next && parseNodeRef(next)?.kind === "protocol_version") {
        localVersionRef.value = next;
      }
    },
    { immediate: true }
  );

  const {
    studyLoading: loading,
    studyError: error,
    noStudy,
  } = useKernelStudyRef(() => props.studyCode, {
    onReset: () => {
      versions.value = [];
      localVersionRef.value = "";
      soa.value = null;
      soaError.value = "";
    },
    onResolved: async (study, { isCurrent }) => {
      const listed = await kernelApi.listProtocolVersions(study);
      if (!isCurrent()) return;
      versions.value = listed;
      for (const v of listed) {
        if (v.ref && !isMasked(v)) {
          seedNodeSummary(v.ref, {
            kind: "protocol_version",
            title: `Version ${v.number}`,
            status: v.status,
          });
        }
      }
      if (!localVersionRef.value) {
        localVersionRef.value = pickDefaultVersionRef(listed);
      }
    },
  });

  function seedScheduleNodeSummaries(matrix: KernelScheduleOfActivities) {
    const toResolve: string[] = [];
    for (const epoch of matrix.epochs ?? []) {
      if (epoch.protocol_element && parseNodeRef(epoch.protocol_element)) {
        seedNodeSummary(epoch.protocol_element, {
          kind: "protocol_element",
          title: epoch.name,
          code: epoch.epoch_id,
        });
      }
    }
    for (const col of matrix.columns ?? []) {
      if (col.protocol_element && parseNodeRef(col.protocol_element)) {
        seedNodeSummary(col.protocol_element, {
          kind: "protocol_element",
          title: col.name,
          code: col.activity_id,
        });
      }
    }
    for (const row of matrix.rows ?? []) {
      if (row.protocol_element && parseNodeRef(row.protocol_element)) {
        seedNodeSummary(row.protocol_element, {
          kind: "protocol_element",
          title: row.name,
          code: row.collection_spec_id,
        });
      }
      if (
        row.form_version_snapshot &&
        parseNodeRef(row.form_version_snapshot)
      ) {
        seedNodeSummary(row.form_version_snapshot, {
          kind: "form_version_snapshot",
          title: `${row.name} form`,
          code: row.collection_spec_id,
        });
      }
      for (const concept of row.concepts ?? []) {
        const normalized = normalizeLookupNodeRef(concept);
        if (normalized) {
          toResolve.push(normalized);
        }
      }
    }
    if (toResolve.length) {
      ensureNodesResolved(toResolve);
    }
  }

  async function loadSchedule(versionRef: string) {
    if (
      !versionRef ||
      typeof kernelApi.getScheduleOfActivities !== "function"
    ) {
      soa.value = null;
      return;
    }
    const req = ++soaRequest;
    soaLoading.value = true;
    soaError.value = "";
    try {
      const loaded = await kernelApi.getScheduleOfActivities(versionRef);
      if (req !== soaRequest) return;
      soa.value = loaded;
      seedScheduleNodeSummaries(loaded);
    } catch (err) {
      if (req === soaRequest) {
        soa.value = null;
        soaError.value = describeKernelError(err);
      }
    } finally {
      if (req === soaRequest) {
        soaLoading.value = false;
      }
    }
  }

  watch(
    activeVersionRef,
    (versionRef) => {
      if (versionRef) {
        void loadSchedule(versionRef);
      } else {
        soa.value = null;
      }
    },
    { immediate: true }
  );

  const epochSpans = computed(() => {
    const matrix = soa.value;
    if (!matrix || !matrix.columns.length) return [];
    const epochsById = new Map(
      (matrix.epochs ?? []).map((ep) => [ep.epoch_id, ep])
    );
    const groups: Array<{
      key: string;
      name: string;
      span: number;
      protocolElement?: string | null;
    }> = [];
    for (const col of matrix.columns) {
      const ep = epochsById.get(col.epoch_id);
      const name = ep?.name || col.epoch_id;
      const prev = groups[groups.length - 1];
      if (prev && prev.key === col.epoch_id) {
        prev.span += 1;
      } else {
        groups.push({
          key: col.epoch_id,
          name,
          span: 1,
          protocolElement: ep?.protocol_element ?? null,
        });
      }
    }
    return groups;
  });

  function isScheduled(row: KernelSoaRow, colIdx: number): boolean {
    return (row.columns ?? []).includes(colIdx);
  }

  function formatVisitTiming(col: KernelSoaColumn): string {
    if (col.study_day === null || col.study_day === undefined) {
      return "Event-driven";
    }
    const wb = Number(col.window_before ?? 0);
    const wa = Number(col.window_after ?? 0);
    const win = wb || wa ? ` (-${wb}/+${wa}d)` : "";
    return `Day ${col.study_day}${win}`;
  }

  function formatRowFrequency(row: KernelSoaRow): string {
    const freq = Number(row.frequency ?? row.columns?.length ?? 0);
    if (freq === 0) return "Unscheduled / log";
    return `${freq} ${freq === 1 ? "visit" : "visits"}`;
  }

  function cellAriaLabel(
    row: KernelSoaRow,
    col: KernelSoaColumn,
    scheduled: boolean
  ): string {
    return `${row.name} at ${col.name}: ${scheduled ? "Scheduled" : "Not scheduled"}. Inspect lineage`;
  }

  function resolveGovernedOrVersionRef(
    ...candidates: Array<string | null | undefined>
  ): string {
    for (const candidate of candidates) {
      if (candidate && parseNodeRef(candidate)) return candidate;
    }
    return activeVersionRef.value;
  }

  function onVersionChange(event: Event) {
    const next = (event.target as HTMLSelectElement).value;
    if (!next) return;
    localVersionRef.value = next;
    onSelectNode(next);
  }

  function onColumnClick(col: KernelSoaColumn) {
    const target = resolveGovernedOrVersionRef(col.protocol_element);
    if (target) onSelectNode(target);
  }

  function onRowClick(row: KernelSoaRow) {
    const target = resolveGovernedOrVersionRef(row.protocol_element);
    if (target) onSelectNode(target);
  }

  function onCellClick(row: KernelSoaRow, col: KernelSoaColumn) {
    const target = resolveGovernedOrVersionRef(
      row.protocol_element,
      col.protocol_element
    );
    if (target) onSelectNode(target);
  }

  const statusLine = computed(() => {
    if (loading.value || soaLoading.value) {
      return "Loading schedule of activities…";
    }
    if (noStudy.value) {
      return `Study ${props.studyCode} wasn't found or isn't visible to your account.`;
    }
    if (!visibleVersions.value.length) {
      return `${props.studyCode} has no protocol versions yet.`;
    }
    if (!soa.value) {
      return "Select a protocol version to view its schedule of activities.";
    }
    const visitCount = soa.value.columns.length;
    const formCount = soa.value.rows.length;
    const epochCount = soa.value.epochs.length;
    return `${epochCount} ${epochCount === 1 ? "epoch" : "epochs"}, ${visitCount} ${visitCount === 1 ? "visit" : "visits"}, ${formCount} ${formCount === 1 ? "collection spec" : "collection specs"}.`;
  });

  return {
    loading,
    error,
    noStudy,
    soaLoading,
    soaError,
    soa,
    visibleVersions,
    activeVersionRef,
    activeVersion,
    parentVersionNodeRef,
    parentVersionLabel,
    epochSpans,
    statusLine,
    helpers: {
      isScheduled,
      formatVisitTiming,
      formatRowFrequency,
      cellAriaLabel,
      onVersionChange,
      onColumnClick,
      onRowClick,
      onCellClick,
    },
  };
}
