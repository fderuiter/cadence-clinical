import FormEntryWorkspace from "../../components/canvas/FormEntryWorkspace.vue";
import type { CanvasWorkspaceDescriptor } from "./types";

const formWorkspace: CanvasWorkspaceDescriptor = {
  id: "form",
  order: 30,
  matches: (ctx) => ctx.hasOpenForm,
  resolveLabel: (ctx) => ctx.zoomFormLabel,
  resolveBranch: (ctx) => {
    const code = ctx.studyCode;
    if (
      ctx.isNewForm &&
      ctx.recordRefId &&
      !ctx.recordParticipant &&
      ctx.isRecordParticipantLoading
    ) {
      return {
        key: `study:${code}:form:record-loading`,
        groupKey: `study:${code}:form`,
        depth: 3,
        targets: ["#record-loading"],
      };
    }
    if (ctx.isNewForm && ctx.recordRefId && !ctx.recordParticipant) {
      return {
        key: `study:${code}:form:record-not-loaded`,
        groupKey: `study:${code}:form`,
        depth: 3,
        targets: ["#record-not-loaded"],
      };
    }
    const formTargetKey = ctx.openFormId || "new";
    return {
      key: `study:${code}:form:${formTargetKey}`,
      groupKey: `study:${code}:form`,
      depth: 3,
      targets: [
        ...(ctx.highlightItemQuery
          ? [`#ecrf-item-${ctx.highlightItemQuery}`]
          : []),
        "#canvas-form-workspace-title",
      ],
    };
  },
  resolveRender: (ctx) => ({
    component: FormEntryWorkspace,
    props: {
      studyCode: ctx.studyCode,
      isNewForm: ctx.isNewForm,
      hasRecordRef: Boolean(ctx.recordRefId),
      recordParticipant: ctx.recordParticipant,
      isRecordParticipantLoading: ctx.isRecordParticipantLoading,
      participantsLocation: ctx.participantsLocation,
      participant: ctx.activeFormParticipant,
      enrollmentId: ctx.activeEnrollmentId,
      recordLoading: ctx.canvas.record.loading,
      formId: ctx.openFormId,
      collectionSpecId: ctx.initialSpecId || null,
      plannedActivityId: ctx.initialVisitId || null,
      formLabel: ctx.loadedFormLabel,
      canManageQueries: ctx.canManage,
      canAnswerQueries: ctx.canAnswer,
      canVerifySource: ctx.canVerify,
      highlightItemId: ctx.highlightItemQuery || null,
      backLocation: ctx.formBackLocation,
      backLabel: ctx.formBackLabel,
      handoffLocation:
        ctx.isDemo && ctx.activeFormParticipant
          ? ctx.ecrfHandoffLocationFor(ctx.activeFormParticipant)
          : null,
      formLocation: ctx.formLocationById,
      newFormCellLocation: ctx.activeFormParticipant
        ? (specId: string, visitId?: string | null) =>
            ctx.newFormCellLocationFor(
              ctx.activeFormParticipant!,
              specId,
              visitId
            )
        : undefined,
      observationLocation: ctx.observationLocationFromForm,
    },
    listeners: {
      "dirty-change": ctx.onFormDirtyChange as (...args: never[]) => unknown,
      "form-loaded": ctx.onFormLoaded as (...args: never[]) => unknown,
      closed: ctx.onFormClosed as (...args: never[]) => unknown,
    },
  }),
};

export default formWorkspace;
