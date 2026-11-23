import QuestionnaireView from "../../views/QuestionnaireView.vue";

export default {
  order: 20,
  availability: "demo",
  title: "Questionnaire",
  activeTabId: "tasks",
  route: {
    path: "/questionnaire",
    name: "questionnaire",
    component: QuestionnaireView,
    meta: { viewId: "view-questionnaire", requiresActiveQuestionnaire: true },
  },
};
