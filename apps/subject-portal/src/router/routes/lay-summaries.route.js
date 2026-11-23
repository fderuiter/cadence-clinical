import { ScrollText } from "lucide-vue-next";
import { markRaw } from "vue";
import LaySummariesView from "../../views/LaySummariesView.vue";

export default {
  order: 70,
  availability: "demo",
  title: "Plain-Language Clinical Trial Results",
  route: {
    path: "/lay-summaries",
    name: "lay-summaries",
    component: LaySummariesView,
    meta: {
      viewId: "view-lay-summaries",
      tab: {
        id: "lay-summaries",
        label: "Trial Results",
        icon: markRaw(ScrollText),
      },
    },
  },
};
