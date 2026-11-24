import StudyHomeWorkspace from "../../components/canvas/StudyHomeWorkspace.vue";
import type { CanvasWorkspaceDescriptor } from "./types";

const studyHomeWorkspace: CanvasWorkspaceDescriptor = {
  id: "study-home",
  order: 60,
  matches: (ctx) => !ctx.stage,
  resolveLabel: (ctx) => `${ctx.studyCode} home`,
  resolveBranch: (ctx) => ({
    key: `study:${ctx.studyCode}:home`,
    groupKey: `study:${ctx.studyCode}:home`,
    depth: 1,
    targets: ["#study-pulse-title"],
  }),
  loadOnEnter: (ctx) => {
    if (!ctx.canvas.pulse && !ctx.canvas.pulseLoading) {
      ctx.canvas.loadPulse();
    }
  },
  resolveRender: (ctx) => ({
    component: StudyHomeWorkspace,
    props: {
      studyCode: ctx.studyCode,
      canManageSites: ctx.canManageSites,
      manageSitesLocation: ctx.sectionLocationForStage("conduct", "sites"),
      activeSite: ctx.sectionContext.activeSite,
      activeParticipant: ctx.sectionContext.activeParticipantScopeParam,
      activeNode: ctx.sectionContext.activeInspectorParticipantNodeParam,
    },
    listeners: {
      drill: ctx.onDrillToParticipants as (...args: never[]) => unknown,
      "open-participants": ctx.onOpenParticipantsForSite as (
        ...args: never[]
      ) => unknown,
    },
  }),
};

export default studyHomeWorkspace;
