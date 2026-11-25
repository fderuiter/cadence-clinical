import { Wallet } from "lucide-vue-next";
import { markRaw } from "vue";
import WalletView from "../../views/WalletView.vue";

export default {
  order: 60,
  availability: "authenticated",
  title: "Participant Financial Wallet",
  route: {
    path: "/wallet",
    name: "wallet",
    component: WalletView,
    meta: {
      viewId: "view-wallet",
      tab: { id: "wallet", label: "My Wallet", icon: markRaw(Wallet) },
    },
  },
};
