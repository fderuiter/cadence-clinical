import ProtocolVersionsSection from "../../components/canvas/ProtocolVersionsSection.vue";
import type { CanvasSectionDescriptor } from "./types";

const protocolVersionsSection: CanvasSectionDescriptor = {
  id: "protocol-versions",
  stageId: "design",
  order: 10,
  label: "Protocol versions",
  description:
    "The protocol's versions, and what each amendment changes and affects",
  focusTargets: ["#protocol-versions-title"],
  resolveRender: (ctx) => ({
    component: ProtocolVersionsSection,
    key: ctx.studyCode,
    props: {
      studyCode: ctx.studyCode,
      selectedRef: ctx.selectedVersionText,
    },
    listeners: {
      select: ctx.onSelectVersion as (...args: never[]) => unknown,
    },
  }),
};

export default protocolVersionsSection;
