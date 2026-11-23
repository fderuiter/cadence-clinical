import ParticipantLoadingWorkspace from "../../components/canvas/ParticipantLoadingWorkspace.vue";
import type { CanvasWorkspaceDescriptor } from "./types";

const resolvingSubjectWorkspace: CanvasWorkspaceDescriptor = {
  id: "resolving-subject",
  order: 50,
  matches: (ctx) => ctx.isResolvingConductSubject,
  resolveLabel: (ctx) => {
    if (!ctx.stage) return `${ctx.studyCode} home`;
    return ctx.section
      ? `${ctx.stage.label}: ${ctx.section.label}`
      : ctx.stage.label;
  },
  resolveBranch: (ctx) => ({
    key: `study:${ctx.studyCode}:resolving-subject`,
    groupKey: `study:${ctx.studyCode}:resolving-subject`,
    depth: 2,
    targets: ["#record-loading"],
  }),
  resolveRender: () => ({
    component: ParticipantLoadingWorkspace,
  }),
};

export default resolvingSubjectWorkspace;
