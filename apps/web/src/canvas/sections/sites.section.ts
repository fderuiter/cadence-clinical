import StudySitesPanel from "../../components/canvas/StudySitesPanel.vue";
import type { CanvasSectionDescriptor } from "./types";

const sitesSection: CanvasSectionDescriptor = {
  id: "sites",
  stageId: "start",
  order: 10,
  label: "Sites",
  description:
    "Investigational sites linked to this study, site institutions, and country registrations",
  focusTargets: ["#study-sites-title"],
  resolveRender: (ctx) => ({
    component: StudySitesPanel,
    key: `sites-${ctx.studyCode}`,
    props: {
      studyCode: ctx.studyCode,
      canManageSites: ctx.canManageSites,
    },
    listeners: {
      "open-participants": ctx.onOpenParticipantsForSite as (
        ...args: never[]
      ) => unknown,
    },
  }),
};

export default sitesSection;
