import ValidationSection from "../../components/canvas/ValidationSection.vue";
import type { CanvasSectionDescriptor } from "./types";

const validationSection: CanvasSectionDescriptor = {
  id: "validation",
  stageId: "data",
  order: 30,
  label: "Validation",
  description:
    "Edit check rule packs, pre-activation previews, study pinning, and validation findings",
  isDataWorklist: true,
  focusTargets: ["#study-validation-title"],
  resolveRender: (ctx) => ({
    component: ValidationSection,
    key: `validation-${ctx.studyCode}`,
    props: {
      studyCode: ctx.studyCode,
      canManageRules: ctx.canManageRules,
      site: ctx.activeSite,
      participant: ctx.activeParticipantScopeParam,
      node: ctx.activeInspectorParticipantNodeParam,
    },
  }),
};

export default validationSection;
