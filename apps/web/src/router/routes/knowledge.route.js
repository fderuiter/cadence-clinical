import { STAFF_HUB_ROLES } from "../helpers.js";

export default {
  order: 380,
  availability: "available",
  contextLabel: "Knowledge",
  hubRoles: STAFF_HUB_ROLES,
  palette: {
    key: "workspace:knowledge",
    order: 70,
    label: "Knowledge & procedures",
    detail: "Published standard operating procedures and version history",
    scopeMode: "workflow",
  },
  catalog: {
    title: "Knowledge & SOP Hub",
    family: "Assure",
    minimumTier: "Complete",
    isBaselineCapability: false,
    roadmapStage:
      "Contextual help, published SOP repository, immutable version snapshots, and Part 11 audit ledger active; multi-state Markdown authoring in pre-release",
    summary:
      "Standard operating procedures, study manuals, and version-controlled guidance.",
  },
  route: {
    path: "/knowledge",
    name: "knowledge",
    component: () => import("../../views/KnowledgeView.vue"),
    meta: {
      requiresAuth: true,
    },
  },
};
