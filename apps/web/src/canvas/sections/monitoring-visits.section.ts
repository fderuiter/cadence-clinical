import CtmsMonitoringSection from "../../components/clinical/CtmsMonitoringSection.vue";
import type { CanvasSectionDescriptor } from "./types";

const monitoringVisitsSection: CanvasSectionDescriptor = {
  id: "monitoring-visits",
  stageId: "conduct",
  order: 20,
  label: "Monitoring visits",
  description:
    "Site monitoring visit schedules, trip findings, compiled letters, and supervisory sign-off",
  focusTargets: ["#ctms-monitoring-title"],
  resolveRender: (ctx) => ({
    component: CtmsMonitoringSection,
    key: `monitoring-${ctx.studyCode}`,
    props: {
      studyCode: ctx.studyCode,
      siteId: ctx.siteFilter,
    },
  }),
};

export default monitoringVisitsSection;
