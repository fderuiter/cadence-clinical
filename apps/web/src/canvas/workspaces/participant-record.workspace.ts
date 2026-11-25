import ParticipantRecordWorkspace from "../../components/canvas/ParticipantRecordWorkspace.vue";
import type { CanvasWorkspaceDescriptor } from "./types";

const participantRecordWorkspace: CanvasWorkspaceDescriptor = {
  id: "participant-record",
  order: 40,
  matches: (ctx) => ctx.isParticipantRecordOpen,
  resolveLabel: (ctx) =>
    `${ctx.recordParticipant?.subject_id ?? (ctx.participantCodeQuery || "Participant")} data`,
  resolveBranch: (ctx) => {
    const code = ctx.studyCode;
    const recId = ctx.recordRefId || ctx.participantCodeQuery || "participant";
    if (ctx.recordParticipant) {
      return {
        key: `study:${code}:record:${recId}:ready`,
        groupKey: `study:${code}:record:${recId}`,
        depth: 2,
        targets: ["#participant-record-title"],
      };
    }
    if (ctx.isRecordParticipantLoading) {
      return {
        key: `study:${code}:record:${recId}:loading`,
        groupKey: `study:${code}:record:${recId}`,
        depth: 2,
        targets: ["#record-loading"],
      };
    }
    return {
      key: `study:${code}:record:${recId}:missing`,
      groupKey: `study:${code}:record:${recId}`,
      depth: 2,
      targets: ["#record-not-loaded"],
    };
  },
  resolveRender: (ctx) => ({
    component: ParticipantRecordWorkspace,
    props: {
      studyCode: ctx.studyCode,
      participant: ctx.recordParticipant,
      loading: ctx.isRecordParticipantLoading,
      canManage: ctx.canManage,
      participantsLocation: ctx.participantsLocation,
      formLocation: ctx.formLocationById,
      newFormLocation: ctx.recordParticipant
        ? ctx.newFormLocationFor(ctx.recordParticipant)
        : undefined,
      newFormCellLocation: ctx.recordParticipant
        ? (specId: string, visitId?: string | null) =>
            ctx.newFormCellLocationFor(ctx.recordParticipant!, specId, visitId)
        : undefined,
      handoffLocation:
        ctx.isDemo && ctx.recordParticipant
          ? ctx.ecrfHandoffLocationFor(ctx.recordParticipant)
          : null,
      observationLocation: ctx.observationLocationFromRecord as (
        observationId: string,
        formId: string,
        itemId?: string | null
      ) => ReturnType<typeof ctx.observationLocationFromRecord>,
    },
    listeners: {
      retry: ctx.onRetryParticipantRecord as (...args: never[]) => unknown,
    },
  }),
};

export default participantRecordWorkspace;
