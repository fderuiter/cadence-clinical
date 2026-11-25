import { TrendingUp } from "lucide-vue-next";
import { markRaw } from "vue";
import ComplianceView from "../../views/ComplianceView.vue";

export default {
  order: 30,
  availability: "demo",
  title: "My Compliance",
  route: {
    path: "/compliance",
    name: "compliance",
    component: ComplianceView,
    meta: {
      viewId: "view-compliance",
      tab: {
        id: "compliance",
        label: "My Compliance",
        icon: markRaw(TrendingUp),
      },
    },
  },
};
