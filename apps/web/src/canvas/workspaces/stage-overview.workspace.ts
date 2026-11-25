import StageOverviewWorkspace from "../../components/canvas/StageOverviewWorkspace.vue";
import type { CanvasWorkspaceDescriptor } from "./types";

const stageOverviewWorkspace: CanvasWorkspaceDescriptor = {
  id: "stage-overview",
  order: 80,
  matches: (ctx) => Boolean(ctx.stage),
  resolveLabel: (ctx) => ctx.stage!.label,
  resolveBranch: (ctx) => ({
    key: `study:${ctx.studyCode}:stage:${ctx.stage!.id}`,
    groupKey: `study:${ctx.studyCode}:stage:${ctx.stage!.id}`,
    depth: 1,
    targets: ["#stage-overview-title"],
  }),
  resolveRender: (ctx) => ({
    component: StageOverviewWorkspace,
    key: `stage-overview-${ctx.studyCode}-${ctx.stage!.id}`,
    props: {
      stage: ctx.stage!,
      sectionLocation: (sectionId: string) =>
        ctx.sectionLocationForStage(ctx.stage!.id, sectionId),
    },
  }),
};

export default stageOverviewWorkspace;
