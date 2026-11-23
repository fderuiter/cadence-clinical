import DocumentsCanvasSection from "../../components/canvas/DocumentsCanvasSection.vue";
import type { CanvasSectionDescriptor } from "./types";

const documentsSection: CanvasSectionDescriptor = {
  id: "documents",
  stageId: "start",
  order: 40,
  label: "Documents",
  description:
    "Study and site essential document binder, intelligence staging, and clinical QC approval",
  focusTargets: [
    "#document-grid-heading",
    "#cra-qc-queue-heading",
    "#btn-etmf-tab-binder",
  ],
  resolveRender: (ctx) => ({
    component: DocumentsCanvasSection,
    key: `docs-${ctx.studyCode}`,
    props: {
      studyCode: ctx.studyCode,
      siteId: ctx.siteFilter,
      canReadDocuments: ctx.canReadDocuments,
    },
  }),
};

export default documentsSection;
