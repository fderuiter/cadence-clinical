import CtmsMilestonesSection from "../../components/clinical/CtmsMilestonesSection.vue";
import type { CanvasSectionDescriptor } from "./types";

const siteMilestonesSection: CanvasSectionDescriptor = {
  id: "site-milestones",
  stageId: "start",
  order: 60,
  label: "Site milestones",
  description:
    "Planned and achieved startup and operational milestones by site",
  focusTargets: ["#ctms-milestones-title"],
  resolveRender: (ctx) => ({
    component: CtmsMilestonesSection,
    key: `milestones-${ctx.studyCode}`,
    props: {
      studyCode: ctx.studyCode,
      siteId: ctx.siteFilter,
    },
  }),
};

export default siteMilestonesSection;
