// Signed-in participant session and the local security PIN screens.
import {
  DEFAULT_DEMO_PARTICIPANT,
  DEFAULT_DEMO_SITE,
  DEMO_SPONSOR,
  DEMO_STUDY,
} from "demo-fixtures";
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
        userId: demo ? DEFAULT_DEMO_PARTICIPANT?.subjectCode || null : null,
        studyId: demo ? DEMO_STUDY?.code || null : null,
        siteId: demo ? DEFAULT_DEMO_SITE?.code || null : null,
        sponsorCode: demo ? DEMO_SPONSOR?.code || null : null,
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
        failedAttempts: 0,
        maxAttempts: 5,
        isLockedOut: false,
        requiresReauth: false,
      },
    };
  },
});
