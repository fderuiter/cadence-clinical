import CtmsGreenlightSection from "../../components/clinical/CtmsGreenlightSection.vue";
import type { CanvasSectionDescriptor } from "./types";

const startupGreenlightSection: CanvasSectionDescriptor = {
  id: "startup-greenlight",
  stageId: "start",
  order: 30,
  label: "Startup readiness",
  description:
    "Six-facet country and site readiness gates and Part 11 certification",
  focusTargets: ["#ctms-greenlight-title"],
  resolveRender: (ctx) => ({
    component: CtmsGreenlightSection,
    key: `greenlight-${ctx.studyCode}`,
    props: {
      studyCode: ctx.studyCode,
      siteId: ctx.siteFilter,
    },
  }),
};

export default startupGreenlightSection;
