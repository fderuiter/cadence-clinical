import QueriesSection from "../../components/canvas/QueriesSection.vue";
import type { CanvasSectionDescriptor } from "./types";

const queriesSection: CanvasSectionDescriptor = {
  id: "queries",
  stageId: "data",
  order: 10,
  label: "Queries",
  description: "Data queries across visible enrollments in this study",
  isDataWorklist: true,
  focusTargets: ["#study-queries-title"],
  resolveRender: (ctx) => ({
    component: QueriesSection,
    key: `queries-${ctx.studyCode}`,
    props: {
      studyCode: ctx.studyCode,
      canManage: ctx.canManage,
      canAnswer: ctx.canAnswer,
      status: ctx.queryStatusFilter,
      owner: ctx.queryOwnerFilter,
      origin: ctx.queryOriginFilter,
      priority: ctx.queryPriorityFilter,
      escalated: ctx.queryEscalatedFilter,
      assignee: ctx.queryAssigneeFilter,
      site: ctx.siteFilter,
      participant: ctx.queryParticipantFilter,
      changed: ctx.queryChangedFilter,
    },
    listeners: {
      filter: ctx.onApplyStudyQueryFilter as (...args: never[]) => unknown,
    },
  }),
};

export default queriesSection;
