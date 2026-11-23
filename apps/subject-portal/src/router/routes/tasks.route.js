import { ClipboardList } from "lucide-vue-next";
import { markRaw } from "vue";
import TasksView from "../../views/TasksView.vue";

export default {
  order: 10,
  availability: "demo",
  title: "My Tasks",
  route: {
    path: "/",
    alias: ["/tasks"],
    name: "tasks",
    component: TasksView,
    meta: {
      viewId: "view-tasks",
      tab: { id: "tasks", label: "My Tasks", icon: markRaw(ClipboardList) },
    },
  },
};
