import SitesSection from "../../components/canvas/SitesSection.vue";
import type { CanvasSectionDescriptor } from "./types";

const sitesSection: CanvasSectionDescriptor = {
  id: "sites",
  stageId: "conduct",
  order: 15,
  label: "Sites",
  description:
    "Clinical sites registered in your workspace and linked to this study",
  focusTargets: ["#sites-section-title", "#study-sites-title"],
  resolveRender: (ctx) => ({
    component: SitesSection,
    key: `sites-${ctx.studyCode}`,
    props: {
      studyCode: ctx.studyCode,
      siteId: ctx.siteFilter,
    },
    listeners: {
      "open-participants": ctx.onOpenParticipantsForSite as (
        ...args: never[]
      ) => unknown,
      "open-activation": ctx.onOpenActivationForSite as (
        ...args: never[]
      ) => unknown,
    },
  }),
};

export default sitesSection;
