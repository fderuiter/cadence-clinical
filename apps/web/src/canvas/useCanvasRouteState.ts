import { computed, ref, type ComputedRef, type Ref } from "vue";
import type {
  LocationQueryRaw,
  RouteLocationNormalizedLoaded,
} from "vue-router";
import type { EcrfForm } from "../api/ecrfFormsApi";
import type { SubjectSummary } from "../api/executionApi";
import { kernelApi, type KernelObservation } from "../api/kernelApi";
import { useCanvasSelection } from "../composables/useCanvasSelection";
import {
  enrollmentIdFromParticipant,
  participantNodeRef,
  type useStudyCanvasStore,
} from "../stores/studyCanvas";
import { isDemoBuild } from "../utils/demoMode";
import { findSection, findStage, isParticipantState } from "./lifecycle";
import {
  collectionInstanceRef,
  formatNodeRef,
  observationNodeRef,
  type NodeRef,
} from "./nodeRef";
import { canResolveNodesRemotely } from "./nodeResolver";
import { findSectionDescriptor } from "./sections/registry";

export interface StudyQuery {
  stage?: string;
  section?: string;
  status?: string;
  owner?: string;
  origin?: string;
  priority?: string;
  escalated?: string;
  assignee?: string;
  site?: string;
  q?: string;
  subject?: string;
  changed?: string;
  node?: string;
  participant?: string;
  form?: string;
  item?: string;
  spec?: string;
  visit?: string;
  observation?: string;
  proposal?: string;
  ask?: string;
}

export const singleQueryString = (value: unknown): string =>
  typeof value === "string" ? value : "";

export function formatFormLabel(form: EcrfForm): string {
  const title = form.collection_title || form.collection_spec_id;
  return form.planned_activity_id
    ? `${title} · ${form.planned_activity_id}`
    : title;
}

export function useCanvasRouteState(options: {
  route: RouteLocationNormalizedLoaded;
  canvas: ReturnType<typeof useStudyCanvasStore>;
}) {
  const { route, canvas } = options;

  const studyCode = computed(() =>
    singleQueryString(route.params.studyCode).trim()
  );
  const sectionMatch = computed(() => findSection(route.query.section));
  const section = computed(() => sectionMatch.value?.section);
  const sectionDescriptor = computed(() =>
    findSectionDescriptor(section.value?.id)
  );
  const stage = computed(
    () => sectionMatch.value?.stage ?? findStage(route.query.stage)
  );

  const statusFilter = computed(() => {
    const status = singleQueryString(route.query.status);
    return isParticipantState(status) ? status : "";
  });
  const queryStatusFilter = computed(() => {
    const status = singleQueryString(route.query.status).toLowerCase();
    return ["open", "answered", "closed", "cancelled"].includes(status)
      ? status
      : "";
  });
  const queryOwnerFilter = computed(() => {
    const owner = singleQueryString(route.query.owner).toLowerCase();
    return ["site", "monitor", "data_manager"].includes(owner) ? owner : "";
  });
  const queryOriginFilter = computed(() => {
    const origin = singleQueryString(route.query.origin).toLowerCase();
    return ["manual", "system"].includes(origin) ? origin : "";
  });
  const queryPriorityFilter = computed(() => {
    const priority = singleQueryString(route.query.priority).toLowerCase();
    return ["low", "normal", "high"].includes(priority) ? priority : "";
  });
  const queryEscalatedFilter = computed(() => {
    const escalated = singleQueryString(route.query.escalated).toLowerCase();
    return escalated === "true" || escalated === "false" ? escalated : "";
  });
  const queryAssigneeFilter = computed(() =>
    singleQueryString(route.query.assignee).trim()
  );
  const siteFilter = computed(() =>
    (
      singleQueryString(route.query.site) ||
      singleQueryString(route.query.siteId)
    ).trim()
  );
  const participantSearchFilter = computed(() =>
    singleQueryString(route.query.q).trim()
  );
  const queryParticipantFilter = computed(() =>
    singleQueryString(route.query.subject).trim()
  );
  const queryChangedFilter = computed(() => {
    const changed = singleQueryString(route.query.changed).toLowerCase();
    return changed === "true" || changed === "false" ? changed : "";
  });

  const loadedForm: Ref<EcrfForm | null> = ref(null);
  const loadedObservation: Ref<KernelObservation | null> = ref(null);
  const resolvedLineage = ref<{ siteCode: string; subjectCode: string } | null>(
    null
  );
  const resolvedParticipantId = ref("");
  let lineageRequest = 0;

  function isParticipantNodePending(node: NodeRef | null | undefined): boolean {
    if (!studyCode.value || !node) return false;
    if (canvas.findKnownParticipant(node)) return false;
    if (node.kind === "enrollment") {
      if (canvas.enrollmentLookups[node.id]) return true;
      return !canvas.failedEnrollmentIds[node.id];
    }
    if (canvas.participantLookups[node.id]) return true;
    return !canvas.failedParticipantIds[node.id];
  }

  const loadedFormParticipant: ComputedRef<SubjectSummary | null> = computed(
    () => {
      const pid = loadedForm.value?.participant_id;
      return pid ? (canvas.knownParticipants[pid] ?? null) : null;
    }
  );
  const lineageParticipant: ComputedRef<SubjectSummary | null> = computed(() =>
    resolvedParticipantId.value
      ? (canvas.knownParticipants[resolvedParticipantId.value] ?? null)
      : null
  );

  const selection = useCanvasSelection({
    query: () => route.query,
    knownParticipants: () => canvas.knownParticipants,
    findParticipant: (node) => canvas.findKnownParticipant(node),
    loadedFormParticipant,
    lineageParticipant,
    isParticipantNodePending,
    canResolveRemotely: canResolveNodesRemotely,
  });

  const {
    explicitNode,
    recordRef,
    recordParticipant: selectionRecordParticipant,
    activeFormParticipant: selectionActiveFormParticipant,
    activeParticipantNodeText: selectionActiveParticipantNodeText,
    isNewForm,
    openFormId,
    openObservationRef,
    proposalRef,
    selectedNode,
    selectedParticipant,
    inspectedParticipant: selectionInspectedParticipant,
  } = selection;

  const rawParticipantQuery = computed(() =>
    singleQueryString(route.query.participant).trim()
  );
  const participantCodeQuery = computed(() =>
    !recordRef.value && !rawParticipantQuery.value.includes(":")
      ? rawParticipantQuery.value
      : ""
  );
  const codeQueryParticipant = computed<SubjectSummary | null>(() => {
    if (!participantCodeQuery.value) return null;
    return (
      Object.values(canvas.knownParticipants).find(
        (item) => item?.subject_id === participantCodeQuery.value
      ) ?? null
    );
  });

  const hasRecordScope = computed(() =>
    Boolean(recordRef.value || participantCodeQuery.value)
  );
  const isParticipantRecordOpen = computed(
    () =>
      hasRecordScope.value && stage.value?.id === "conduct" && !section.value
  );
  const recordParticipant = computed<SubjectSummary | null>(
    () => selectionRecordParticipant.value ?? codeQueryParticipant.value
  );
  const activeFormParticipant = computed<SubjectSummary | null>(
    () => selectionActiveFormParticipant.value ?? codeQueryParticipant.value
  );
  const activeParticipantNodeText = computed(() => {
    if (selectionActiveParticipantNodeText.value) {
      return selectionActiveParticipantNodeText.value;
    }
    if (codeQueryParticipant.value) {
      return participantNodeRef(codeQueryParticipant.value);
    }
    return "";
  });

  const inspectedParticipant = computed<SubjectSummary | null>(
    () =>
      selectionInspectedParticipant.value ??
      (!explicitNode.value &&
      !openObservationRef.value &&
      !openFormId.value &&
      !proposalRef.value
        ? codeQueryParticipant.value
        : null)
  );

  const conductSubjectQuery = computed(() => {
    if (hasRecordScope.value || section.value?.id === "queries") return "";
    if (stage.value && stage.value.id !== "conduct") return "";
    return singleQueryString(route.query.subject).trim();
  });
  const isResolvingConductSubject = ref(false);

  const inspectorContextParticipant = computed<SubjectSummary | null>(
    () =>
      inspectedParticipant.value ??
      (!explicitNode.value ? activeFormParticipant.value : null)
  );

  const highlightItemQuery = computed(() =>
    singleQueryString(route.query.item).trim()
  );
  const initialSpecId = computed(() =>
    singleQueryString(route.query.spec).trim()
  );
  const initialVisitId = computed(() =>
    singleQueryString(route.query.visit).trim()
  );
  const hasOpenForm = computed(
    () => isNewForm.value || Boolean(openFormId.value)
  );
  const askText = computed(() => singleQueryString(route.query.ask).trim());

  const isRecordParticipantLoading = computed(() =>
    recordRef.value
      ? isParticipantNodePending(recordRef.value)
      : Boolean(
          studyCode.value &&
          participantCodeQuery.value &&
          !codeQueryParticipant.value &&
          canvas.participantCodeLookups?.[participantCodeQuery.value]
        )
  );

  const activeEnrollmentId = computed(() => {
    const persisted = enrollmentIdFromParticipant(activeFormParticipant.value);
    if (persisted) return persisted;
    if (
      activeFormParticipant.value &&
      canvas.record.subjectCode === activeFormParticipant.value.subject_id &&
      canvas.record.enrollmentId
    ) {
      return canvas.record.enrollmentId;
    }
    if (recordRef.value?.kind === "enrollment") return recordRef.value.id;
    if (loadedForm.value?.enrollment_id) return loadedForm.value.enrollment_id;
    return null;
  });

  const zoomParticipant = computed(
    () =>
      recordParticipant.value ??
      loadedFormParticipant.value ??
      selectedParticipant.value ??
      lineageParticipant.value
  );
  const zoomParticipantCode = computed(
    () =>
      zoomParticipant.value?.subject_id ||
      participantCodeQuery.value ||
      resolvedLineage.value?.subjectCode ||
      ""
  );
  const zoomSite = computed(
    () =>
      zoomParticipant.value?.site_id ||
      resolvedLineage.value?.siteCode ||
      siteFilter.value ||
      ""
  );
  const zoomCurrent = computed(() => {
    if (openObservationRef.value) return "observation";
    if (hasOpenForm.value) return "form";
    if (zoomParticipantCode.value) {
      return isParticipantRecordOpen.value ? "participant" : "section";
    }
    return zoomSite.value ? "site" : "study";
  });

  const zoomFormLabel = computed(() => {
    if (isNewForm.value) {
      if (initialSpecId.value && initialVisitId.value) {
        return `Forms · ${initialSpecId.value} · ${initialVisitId.value}`;
      }
      if (initialSpecId.value) {
        return `Forms · ${initialSpecId.value}`;
      }
      return "Forms";
    }
    if (loadedForm.value && loadedForm.value.form_id === openFormId.value) {
      return formatFormLabel(loadedForm.value);
    }
    return "Case report form";
  });

  const zoomObservationLabel = computed(
    () => loadedObservation.value?.concept || "Observation"
  );

  const isDataWorklistSection = computed(() =>
    Boolean(sectionDescriptor.value?.isDataWorklist)
  );

  function studyLocation(query: StudyQuery = {}, code = studyCode.value) {
    const clean: LocationQueryRaw = {};
    for (const [key, value] of Object.entries(query)) {
      if (value) clean[key] = value;
    }
    return { name: "study", params: { studyCode: code }, query: clean };
  }

  const activeParticipantScopeParam = computed(() => {
    if (recordRef.value) return formatNodeRef(recordRef.value);
    return participantCodeQuery.value || "";
  });

  const activeInspectorParticipantNodeParam = computed(() => {
    if (selectedNode.value) return formatNodeRef(selectedNode.value);
    if (!activeParticipantScopeParam.value && activeParticipantNodeText.value) {
      return activeParticipantNodeText.value;
    }
    return "";
  });

  function scopedStudyLocation(query: StudyQuery = {}, code = studyCode.value) {
    const preservedSite = siteFilter.value || zoomSite.value || undefined;
    const preservedParticipant = activeParticipantScopeParam.value || undefined;
    const preservedNode =
      activeInspectorParticipantNodeParam.value || undefined;
    return studyLocation(
      {
        site: preservedSite,
        participant: preservedParticipant,
        node: preservedNode,
        ...query,
      },
      code
    );
  }

  function recordLocationFor(participant: SubjectSummary) {
    const text = participantNodeRef(participant);
    return studyLocation({ stage: "conduct", participant: text });
  }

  function newFormLocationFor(participant: SubjectSummary) {
    const text = participantNodeRef(participant);
    return studyLocation({ stage: "conduct", participant: text, form: "new" });
  }

  function newFormCellLocationFor(
    participant: SubjectSummary,
    specId: string,
    visitId?: string | null
  ) {
    const text = participantNodeRef(participant);
    return studyLocation({
      stage: "conduct",
      participant: text,
      form: "new",
      spec: specId || undefined,
      visit: visitId || undefined,
    });
  }

  function ecrfHandoffLocationFor(participant: SubjectSummary) {
    if (!isDemoBuild()) return null;
    const query: LocationQueryRaw = {
      studyId: studyCode.value,
      subjectId: participant.subject_id,
    };
    const activeParticipantMatch =
      activeFormParticipant.value?.id === participant.id;
    const siteIdForHandoff =
      participant.site_id || (activeParticipantMatch ? zoomSite.value : "");
    if (siteIdForHandoff) {
      query.siteId = siteIdForHandoff;
    }
    const enrollmentForParticipant =
      enrollmentIdFromParticipant(participant) ??
      (canvas.record.subjectCode === participant.subject_id
        ? canvas.record.enrollmentId
        : loadedForm.value?.participant_id === participant.id
          ? loadedForm.value.enrollment_id
          : null);
    if (enrollmentForParticipant) {
      query.enrollmentId = enrollmentForParticipant;
    }
    if (activeParticipantMatch && openFormId.value) {
      query.formId = openFormId.value;
    }
    const specId =
      activeParticipantMatch && loadedForm.value?.collection_spec_id
        ? loadedForm.value.collection_spec_id
        : activeParticipantMatch && initialSpecId.value
          ? initialSpecId.value
          : "";
    if (specId) {
      query.collectionSpecId = specId;
    }
    const visitId =
      activeParticipantMatch && loadedForm.value?.planned_activity_id
        ? loadedForm.value.planned_activity_id
        : activeParticipantMatch && initialVisitId.value
          ? initialVisitId.value
          : "";
    if (visitId) {
      query.plannedActivityId = visitId;
      query.visitId = visitId;
    }
    if (activeParticipantMatch && highlightItemQuery.value) {
      query.itemId = highlightItemQuery.value;
    }
    return { name: "ecrf", query };
  }

  function dataEntryLocationFor(participant: SubjectSummary) {
    return newFormLocationFor(participant);
  }

  function formLocationById(formId: string, itemId?: string | null) {
    const formRef = collectionInstanceRef(formId);
    const preserveDataSection = isDataWorklistSection.value;
    return studyLocation({
      stage: preserveDataSection ? stage.value?.id || "data" : "conduct",
      section: preserveDataSection ? section.value!.id : undefined,
      site:
        siteFilter.value ||
        (!activeParticipantNodeText.value
          ? zoomSite.value || undefined
          : undefined),
      participant: activeParticipantNodeText.value || undefined,
      form: formRef ?? undefined,
      item: itemId || undefined,
    });
  }

  function observationLocationFromRecord(
    observationId: string,
    formId?: string,
    itemId?: string | null
  ) {
    const obsRef = observationNodeRef(observationId);
    const formRef = formId ? collectionInstanceRef(formId) : null;
    return studyLocation({
      stage: "conduct",
      site: siteFilter.value || undefined,
      participant: activeParticipantNodeText.value || undefined,
      form: formRef ?? undefined,
      item: itemId || undefined,
      observation: obsRef ?? undefined,
    });
  }

  function observationLocationFromForm(
    observationId: string,
    formId: string,
    itemId?: string | null
  ) {
    const obsRef = observationNodeRef(observationId);
    const formRef = collectionInstanceRef(formId || openFormId.value || "");
    const preserveDataSection = isDataWorklistSection.value;
    return studyLocation({
      stage: preserveDataSection ? stage.value?.id || "data" : "conduct",
      section: preserveDataSection ? section.value!.id : undefined,
      site:
        siteFilter.value ||
        (!activeParticipantNodeText.value
          ? zoomSite.value || undefined
          : undefined),
      participant: activeParticipantNodeText.value || undefined,
      form: formRef ?? undefined,
      item: itemId || highlightItemQuery.value || undefined,
      observation: obsRef ?? undefined,
    });
  }

  const zoomParticipantLocation = computed(() => {
    if (zoomParticipant.value) {
      const base = recordLocationFor(zoomParticipant.value);
      if (siteFilter.value) {
        return studyLocation({
          stage: "conduct",
          site: siteFilter.value,
          participant: base.query.participant as string | undefined,
        });
      }
      return base;
    }
    if (participantCodeQuery.value) {
      return studyLocation({
        stage: "conduct",
        site: zoomSite.value || undefined,
        participant: participantCodeQuery.value,
      });
    }
    return studyLocation({
      stage: "conduct",
      section: "participants",
      site: zoomSite.value || undefined,
    });
  });

  const zoomFormLocation = computed(() => {
    if (openFormId.value) {
      return formLocationById(
        openFormId.value,
        highlightItemQuery.value || undefined
      );
    }
    return studyLocation({
      stage: "conduct",
      participant: activeParticipantNodeText.value || undefined,
      form: "new",
    });
  });

  const formBackLocation = computed(() => {
    if (isDataWorklistSection.value && section.value) {
      return studyLocation({
        stage: stage.value?.id || "data",
        section: section.value.id,
        site: siteFilter.value || undefined,
      });
    }
    if (activeFormParticipant.value) {
      return recordLocationFor(activeFormParticipant.value);
    }
    return studyLocation({
      stage: "conduct",
      section: "participants",
      site: zoomSite.value || undefined,
    });
  });

  const formBackLabel = computed(() => {
    if (isDataWorklistSection.value && section.value) {
      return `Back to ${section.value.label}`;
    }
    if (activeFormParticipant.value) {
      return `Back to ${activeFormParticipant.value.subject_id}`;
    }
    if (zoomSite.value) return `Back to Site ${zoomSite.value} participants`;
    return `Back to ${studyCode.value} participants`;
  });

  const observationReturnLocation = computed(() => {
    if (openFormId.value) {
      return formLocationById(
        openFormId.value,
        highlightItemQuery.value || undefined
      );
    }
    if (isDataWorklistSection.value && section.value) {
      return studyLocation({
        stage: stage.value?.id || "data",
        section: section.value.id,
        site: siteFilter.value || undefined,
      });
    }
    if (activeFormParticipant.value) {
      const base = recordLocationFor(activeFormParticipant.value);
      if (siteFilter.value) {
        return studyLocation({
          stage: "conduct",
          site: siteFilter.value,
          participant: base.query.participant as string | undefined,
        });
      }
      return base;
    }
    if (zoomSite.value) {
      return studyLocation({
        stage: "conduct",
        section: "participants",
        site: zoomSite.value,
      });
    }
    return studyLocation();
  });

  const observationReturnLabel = computed(() => {
    if (openFormId.value) {
      return loadedForm.value && loadedForm.value.form_id === openFormId.value
        ? `Back to ${formatFormLabel(loadedForm.value)}`
        : "Back to form";
    }
    if (isDataWorklistSection.value && section.value) {
      return `Back to ${section.value.label}`;
    }
    if (activeFormParticipant.value) {
      return `Back to ${activeFormParticipant.value.subject_id}`;
    }
    if (zoomSite.value) return `Back to Site ${zoomSite.value} participants`;
    return `Back to ${studyCode.value} home`;
  });

  async function resolveEnrollmentLineage(
    enrollmentRef: string
  ): Promise<{ req: number; siteCode: string; subjectCode: string } | null> {
    if (!enrollmentRef || typeof kernelApi.getZoomPath !== "function") {
      return null;
    }
    const req = ++lineageRequest;
    try {
      const steps = await kernelApi.getZoomPath(enrollmentRef);
      if (req !== lineageRequest) return null;
      const siteStep = steps.find(
        (s) => s.level === "study_site" && s.mask === null && s.label
      );
      const enrollmentStep = steps.find(
        (s) => s.level === "enrollment" && s.mask === null && s.label
      );
      if (siteStep?.label || enrollmentStep?.label) {
        const lineage = {
          siteCode: siteStep?.label ?? "",
          subjectCode: enrollmentStep?.label ?? "",
        };
        resolvedLineage.value = lineage;
        return { req, ...lineage };
      }
    } catch {
      // Keep whatever scope context is already known in the URL.
    }
    return null;
  }

  function isCurrentLineageRequest(req: number): boolean {
    return req === lineageRequest;
  }

  function invalidateLineage(): void {
    resolvedParticipantId.value = "";
    lineageRequest++;
  }

  function resetLoadedRouteContext(): void {
    loadedForm.value = null;
    loadedObservation.value = null;
    resolvedLineage.value = null;
    resolvedParticipantId.value = "";
    lineageRequest++;
  }

  return {
    ...selection,
    studyCode,
    section,
    sectionDescriptor,
    stage,
    statusFilter,
    queryStatusFilter,
    queryOwnerFilter,
    queryOriginFilter,
    queryPriorityFilter,
    queryEscalatedFilter,
    queryAssigneeFilter,
    siteFilter,
    participantSearchFilter,
    queryParticipantFilter,
    queryChangedFilter,
    loadedForm,
    loadedObservation,
    resolvedLineage,
    resolvedParticipantId,
    rawParticipantQuery,
    participantCodeQuery,
    codeQueryParticipant,
    hasRecordScope,
    isParticipantRecordOpen,
    recordParticipant,
    activeFormParticipant,
    activeParticipantNodeText,
    inspectedParticipant,
    inspectorContextParticipant,
    conductSubjectQuery,
    isResolvingConductSubject,
    highlightItemQuery,
    initialSpecId,
    initialVisitId,
    hasOpenForm,
    askText,
    isRecordParticipantLoading,
    activeEnrollmentId,
    zoomParticipant,
    zoomParticipantCode,
    zoomSite,
    zoomCurrent,
    zoomFormLabel,
    zoomObservationLabel,
    isDataWorklistSection,
    activeParticipantScopeParam,
    activeInspectorParticipantNodeParam,
    studyLocation,
    scopedStudyLocation,
    recordLocationFor,
    newFormLocationFor,
    newFormCellLocationFor,
    ecrfHandoffLocationFor,
    dataEntryLocationFor,
    formLocationById,
    observationLocationFromRecord,
    observationLocationFromForm,
    zoomParticipantLocation,
    zoomFormLocation,
    formBackLocation,
    formBackLabel,
    observationReturnLocation,
    observationReturnLabel,
    resolveEnrollmentLineage,
    isCurrentLineageRequest,
    invalidateLineage,
    resetLoadedRouteContext,
  };
}
