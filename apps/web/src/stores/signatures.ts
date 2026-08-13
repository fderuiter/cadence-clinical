import { defineStore } from "pinia";
import type { BatchSignPayload, BatchSignResponse } from "../api/execution";

export type { BatchSignPayload, BatchSignResponse };

export const useSignatureStore = defineStore("signatures", {
  state: () => {
    return {
      selectedFormIds: [] as string[],
    };
  },
  actions: {
    resetDemoStorage() {
      if (typeof window !== "undefined" && window.localStorage) {
        window.localStorage.removeItem("lastSignatureResult");
      }
    },
  },
});

