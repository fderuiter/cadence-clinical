import SiteActivationPanel from "../../components/clinical/SiteActivationPanel.vue";
import type { CanvasSectionDescriptor } from "./types";

const siteActivationSection: CanvasSectionDescriptor = {
  id: "site-activation",
  stageId: "start",
  order: 20,
  label: "Site activation",
  description:
    "A site's activation documents and activating the site once they are complete",
  focusTargets: ["#site-activation-title"],
  resolveRender: (ctx) => ({
    component: SiteActivationPanel,
    key: ctx.studyCode,
    props: {
      studyCode: ctx.studyCode,
      siteId: ctx.siteFilter,
    },
  }),
};

export default siteActivationSection;
