import ObservationView from "../../views/ObservationView.vue";
import type { CanvasWorkspaceDescriptor } from "./types";

const observationWorkspace: CanvasWorkspaceDescriptor = {
  id: "observation",
  order: 20,
  matches: (ctx) => Boolean(ctx.openObservationRef),
  resolveLabel: (ctx) => `Observation ${ctx.zoomObservationLabel}`,
  resolveBranch: (ctx) => ({
    key: `study:${ctx.studyCode}:observation:${ctx.openObservationRef}`,
    groupKey: `study:${ctx.studyCode}:observation`,
    depth: 4,
    targets: ["#observation-view-title"],
  }),
  resolveRender: (ctx) => ({
    component: ObservationView,
    key: ctx.openObservationRef,
    props: {
      embedded: true,
      studyCode: ctx.studyCode,
      observationRef: ctx.openObservationRef,
      returnTo: ctx.observationReturnLocation,
      returnLabel: ctx.observationReturnLabel,
    },
    listeners: {
      loaded: ctx.onObservationLoaded as (...args: never[]) => unknown,
    },
  }),
};

export default observationWorkspace;
