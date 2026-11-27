// One route per portal view, in the order the shell renders them. `viewId` is
// the view's section id (and the value of `state.currentView`); `tab` puts the
// view in the tab bar.
import {
  ClipboardList,
  FileSignature,
  Mail,
  ScrollText,
  TrendingUp,
  Wallet,
} from "lucide-vue-next";
import { markRaw } from "vue";

import { isDemoBuild } from "../utils/demoMode.js";
import ComplianceView from "../views/ComplianceView.vue";
import ConsentView from "../views/ConsentView.vue";
import InboxView from "../views/InboxView.vue";
import LaySummariesView from "../views/LaySummariesView.vue";
import QuestionnaireView from "../views/QuestionnaireView.vue";
import TasksView from "../views/TasksView.vue";
import WalletView from "../views/WalletView.vue";

const includeDemoTabIcons = isDemoBuild() || import.meta.env?.MODE === "test";

export const viewRoutes = [
  {
    path: "/",
    alias: ["/tasks"],
    name: "tasks",
    component: TasksView,
    meta: {
      viewId: "view-tasks",
      tab: {
        id: "tasks",
        label: "My Tasks",
        icon: includeDemoTabIcons ? markRaw(ClipboardList) : null,
      },
    },
  },
  {
    path: "/questionnaire",
    name: "questionnaire",
    component: QuestionnaireView,
    // Opened from a task; a direct link without an active questionnaire
    // lands on the task list instead.
    meta: { viewId: "view-questionnaire", requiresActiveQuestionnaire: true },
  },
  {
    path: "/compliance",
    name: "compliance",
    component: ComplianceView,
    meta: {
      viewId: "view-compliance",
      tab: {
        id: "compliance",
        label: "My Compliance",
        icon: includeDemoTabIcons ? markRaw(TrendingUp) : null,
      },
    },
  },
  {
    path: "/inbox",
    name: "inbox",
    component: InboxView,
    meta: {
      viewId: "view-inbox",
      tab: {
        id: "inbox",
        label: "My Inbox",
        icon: includeDemoTabIcons ? markRaw(Mail) : null,
      },
    },
  },
  {
    path: "/consent",
    name: "consent",
    component: ConsentView,
    meta: {
      viewId: "view-consent",
      tab: {
        id: "consent",
        label: "My Consent",
        icon: includeDemoTabIcons ? markRaw(FileSignature) : null,
      },
    },
  },
  {
    path: "/wallet",
    name: "wallet",
    component: WalletView,
    meta: {
      viewId: "view-wallet",
      tab: { id: "wallet", label: "My Wallet", icon: markRaw(Wallet) },
    },
  },
  {
    path: "/lay-summaries",
    name: "lay-summaries",
    component: LaySummariesView,
    meta: {
      viewId: "view-lay-summaries",
      tab: {
        id: "lay-summaries",
        label: "Trial Results",
        icon: includeDemoTabIcons ? markRaw(ScrollText) : null,
      },
    },
  },
];

// The tab bar entries, with the view each one controls and canonical DOM IDs.
export const tabs = viewRoutes
  .filter((route) => route.meta.tab)
  .map((route) => ({
    ...route.meta.tab,
    viewId: route.meta.viewId,
    itemId: `tab-btn-${route.meta.tab.id}`,
    buttonId: `btn-tab-btn-${route.meta.tab.id}`,
  }));

export function activeTabForView(viewId) {
  if (viewId === "view-questionnaire") {
    return tabs.find((tab) => tab.id === "tasks") || null;
  }
  return tabs.find((tab) => tab.viewId === viewId) || null;
}

export function routeForView(viewId) {
  return viewRoutes.find((route) => route.meta.viewId === viewId);
}
