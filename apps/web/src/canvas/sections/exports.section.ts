import ExportsSection from "../../components/canvas/ExportsSection.vue";
import type { CanvasSectionDescriptor } from "./types";

const exportsSection: CanvasSectionDescriptor = {
  id: "exports",
  stageId: "submit",
  order: 10,
  label: "Exports",
  description:
    "Streaming CSV, CDISC ODM v1.3.2 XML, and Dataset-JSON snapshots at a lock or timestamp",
  focusTargets: ["#study-exports-title"],
  resolveRender: (ctx) => ({
    component: ExportsSection,
    key: `exports-${ctx.studyCode}`,
    props: {
      studyCode: ctx.studyCode,
    },
  }),
};

export default exportsSection;
