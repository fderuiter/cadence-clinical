import LocksSection from "../../components/canvas/LocksSection.vue";
import type { CanvasSectionDescriptor } from "./types";

const locksSection: CanvasSectionDescriptor = {
  id: "locks",
  stageId: "data",
  order: 20,
  label: "Locks",
  description:
    "Frozen and locked scopes across the study, with signed apply and release",
  focusTargets: ["#study-locks-title"],
  resolveRender: (ctx) => ({
    component: LocksSection,
    key: `locks-${ctx.studyCode}`,
    props: {
      studyCode: ctx.studyCode,
    },
  }),
};

export default locksSection;
