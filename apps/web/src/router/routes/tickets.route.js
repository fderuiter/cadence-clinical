import { STAFF_HUB_ROLES } from "../helpers.js";

export default {
  order: 360,
  availability: "available",
  contextLabel: "Support",
  hubRoles: STAFF_HUB_ROLES,
  palette: {
    key: "workspace:support",
    order: 60,
    label: "Support tickets",
    detail: "Operational issues, deviations, and resolution sign-off",
    scopeMode: "workflow",
  },
  catalog: {
    title: "Clinical Issues & Tickets",
    family: "Orbit",
    minimumTier: "Select",
    isBaselineCapability: false,
    roadmapStage:
      "Operational issue queue, SLA timers, dual-visibility notes, Part 11 sign-off, and audit trail active; binary evidence storage in pre-release",
    summary:
      "Operational site issue tracking, monitoring action items, and escalation workflows.",
  },
  route: {
    path: "/tickets",
    name: "tickets",
    component: () => import("../../views/TicketsView.vue"),
    meta: {
      requiresAuth: true,
    },
  },
};
