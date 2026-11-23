import StudyTrainingView from "../../views/StudyTrainingView.vue";
import type { CanvasSectionDescriptor } from "./types";

const trainingSection: CanvasSectionDescriptor = {
  id: "training",
  stageId: "start",
  order: 50,
  label: "Training",
  description:
    "Protocol curricula, personnel training assignments, and site delegation logs",
  focusTargets: ["#study-training-title"],
  resolveRender: (ctx) => ({
    component: StudyTrainingView,
    key: `training-${ctx.studyCode}`,
    props: {
      studyId: ctx.studyCode,
      siteId: ctx.siteFilter,
    },
  }),
};

export default trainingSection;
