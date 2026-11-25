import type { CanvasWorkspaceDescriptor } from "./types";

const sectionWorkspace: CanvasWorkspaceDescriptor = {
  id: "section",
  order: 70,
  matches: (ctx) => Boolean(ctx.stage && ctx.section && ctx.sectionDescriptor),
  resolveLabel: (ctx) => `${ctx.stage!.label}: ${ctx.section!.label}`,
  resolveBranch: (ctx) => ({
    key: `study:${ctx.studyCode}:section:${ctx.section!.id}`,
    groupKey: `study:${ctx.studyCode}:section:${ctx.section!.id}`,
    depth: 1,
    targets: ctx.sectionDescriptor?.focusTargets ?? ["#stage-overview-title"],
  }),
  loadOnEnter: (ctx) => {
    ctx.sectionDescriptor?.loadOnEnter?.(ctx.sectionContext);
  },
  resolveRender: (ctx) =>
    ctx.sectionDescriptor!.resolveRender(ctx.sectionContext),
};

export default sectionWorkspace;
