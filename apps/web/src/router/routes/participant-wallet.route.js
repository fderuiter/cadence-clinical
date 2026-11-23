export default {
  order: 400,
  availability: "demo",
  contextLabel: "Participant reimbursements",
  catalog: {
    title: "Participant Financial Wallet",
    family: "Orbit",
    minimumTier: "Select",
    isBaselineCapability: false,
    roadmapStage: "Pre-release Orbit payments roadmap",
    summary:
      "Participant stipend disbursements and visit reimbursement tracking.",
  },
  route: {
    path: "/participant-wallet",
    name: "participant-wallet",
    component: () => import("../../views/ParticipantWalletView.vue"),
    meta: {
      requiresAuth: true,
    },
  },
};
