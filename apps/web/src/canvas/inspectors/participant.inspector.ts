import ParticipantInspectorPane from "../../components/canvas/ParticipantInspectorPane.vue";
import { formatNodeRef, parseNodeRef } from "../nodeRef";
import type { CanvasInspectorDescriptor } from "./types";

function canonicalParticipantEnrollmentRef(
  enrollmentRef: string | null | undefined
): string {
  const parsed = parseNodeRef(enrollmentRef);
  return parsed?.kind === "enrollment"
    ? formatNodeRef({ kind: "enrollment", id: parsed.id })
    : "";
}

const participantInspector: CanvasInspectorDescriptor = {
  id: "participant",
  kinds: ["participant", "enrollment"],
  order: 10,
  matches: (ctx) => Boolean(ctx.participant && !ctx.protocolVersion),
  resolveHeader: (ctx) => {
    const canonicalRef = canonicalParticipantEnrollmentRef(
      ctx.participant?.enrollment_ref
    );
    return {
      kindLabel: "Participant",
      title: ctx.participant!.subject_id,
      isCodeTitle: true,
      activeNodeRef: canonicalRef || ctx.nodeRef,
      kernelRef: canonicalRef || (parseNodeRef(ctx.nodeRef) ? ctx.nodeRef : ""),
      canSimulate: false,
      impactLineageMode: "impact",
    };
  },
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
