import { defineStore } from "pinia";
import type { BatchSignPayload, BatchSignResponse } from "../api/execution";

export type { BatchSignPayload, BatchSignResponse };

/**
 * useSignatureStore - Pinia store managing local signature flow UI state.
 *
 * This store maintains client-side tracking of user-selected forms for the batch
 * signature modal, while asynchronous state tracking has been refactored to use TanStack Query.
 */
export const useSignatureStore = defineStore("signatures", {
  state: () => {
    return {
      selectedFormIds: [] as string[],
    };
  },
  actions: {
    /**
     * Resets local storage item for the last signature result.
     *
     * Used primarily to clear transient mockup session results when switching modes
     * or resetting the client demo state.
     */
    resetDemoStorage() {
      if (typeof window !== "undefined" && window.localStorage) {
        window.localStorage.removeItem("lastSignatureResult");
      }
    },
  },
});
