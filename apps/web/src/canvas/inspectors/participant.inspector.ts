import ParticipantInspectorPane from "../../components/canvas/ParticipantInspectorPane.vue";
import { parseNodeRef } from "../nodeRef";
import type { CanvasInspectorDescriptor } from "./types";

const participantInspector: CanvasInspectorDescriptor = {
  id: "participant",
  kinds: ["participant"],
  order: 10,
  matches: (ctx) => Boolean(ctx.participant && !ctx.protocolVersion),
  resolveHeader: (ctx) => ({
    kindLabel: "Participant",
    title: ctx.participant!.subject_id,
    activeNodeRef: ctx.nodeRef,
    kernelRef: parseNodeRef(ctx.nodeRef) ? ctx.nodeRef : "",
    canSimulate: false,
    impactLineageMode: "impact",
  }),
  resolveDetailsRender: (ctx) => ({
    component: ParticipantInspectorPane,
    key: `participant-${ctx.participant!.id}`,
    props: {
      studyCode: ctx.studyCode,
      participant: ctx.participant!,
      canManageParticipants: ctx.canManageParticipants,
      canUnblindParticipants: ctx.canUnblindParticipants,
      recordLocation: ctx.recordLocation,
      dataEntryLocation: ctx.dataEntryLocation,
      handoffLocation: ctx.handoffLocation,
    },
  }),
};

export default participantInspector;
