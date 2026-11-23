import RtsmSupplySection from "../../components/clinical/RtsmSupplySection.vue";
import type { CanvasSectionDescriptor } from "./types";

const supplySection: CanvasSectionDescriptor = {
  id: "supply",
  stageId: "conduct",
  order: 30,
  label: "Investigational supply",
  description:
    "Chain-of-custody trace, site kit accountability, and randomization schemes",
  focusTargets: ["#rtsm-supply-title"],
  resolveRender: (ctx) => ({
    component: RtsmSupplySection,
    key: `supply-${ctx.studyCode}`,
    props: {
      studyCode: ctx.studyCode,
      siteId: ctx.siteFilter,
    },
  }),
};

export default supplySection;
