import { Mail } from "lucide-vue-next";
import { markRaw } from "vue";
import InboxView from "../../views/InboxView.vue";

export default {
  order: 40,
  availability: "demo",
  title: "My Inbox",
  route: {
    path: "/inbox",
    name: "inbox",
    component: InboxView,
    meta: {
      viewId: "view-inbox",
      tab: { id: "inbox", label: "My Inbox", icon: markRaw(Mail) },
    },
  },
};
