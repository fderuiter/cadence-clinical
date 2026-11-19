// Signed-in participant session and the local security PIN screens.
import { defineStore } from "pinia";

function isInitialDemoBuild(env = import.meta.env) {
  return (
    env?.MODE === "demo" ||
    env?.VITE_DEMO_MODE === "true" ||
    (typeof process !== "undefined" && process.env?.VITE_DEMO_MODE === "true")
  );
}

export const useSessionStore = defineStore("session", {
  state: () => {
    const demo = isInitialDemoBuild();
    return {
      session: {
        userId: demo ? "subject_001" : null,
        roles: "Subject",
        token: null,
        isOfflineMode: demo,
        isDemoMode: demo,
      },
      authError: "",
      pinSetup: {
        isOpen: false,
        pin: "",
        confirmPin: "",
        error: "",
      },
      pinUnlock: {
        isOpen: false,
        pin: "",
        error: "",
        confirmingReset: false,
      },
    };
  },
});
