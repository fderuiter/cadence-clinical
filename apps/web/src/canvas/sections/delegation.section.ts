import CtmsDelegationSection from "../../components/clinical/CtmsDelegationSection.vue";
import type { CanvasSectionDescriptor } from "./types";

const delegationSection: CanvasSectionDescriptor = {
  id: "delegation",
  stageId: "start",
  order: 70,
  label: "Delegation log",
  description:
    "Delegated trial duties, Principal Investigator sign-off, and export",
  focusTargets: ["#ctms-delegation-title"],
  resolveRender: (ctx) => ({
    component: CtmsDelegationSection,
    key: `delegation-${ctx.studyCode}`,
    props: {
      studyCode: ctx.studyCode,
      siteId: ctx.siteFilter,
    },
  }),
};

export default delegationSection;
