import { FileSignature } from "lucide-vue-next";
import { markRaw } from "vue";
import ConsentView from "../../views/ConsentView.vue";

export default {
  order: 50,
  availability: "demo",
  title: "My Consent",
  route: {
    path: "/consent",
    name: "consent",
    component: ConsentView,
    meta: {
      viewId: "view-consent",
      tab: {
        id: "consent",
        label: "My Consent",
        icon: markRaw(FileSignature),
      },
    },
  },
};
