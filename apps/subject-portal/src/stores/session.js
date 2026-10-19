// Signed-in participant session and the local security PIN screens.
import { defineStore } from "pinia";

export const useSessionStore = defineStore("session", {
  state: () => ({
    session: {
      userId: "subject_001",
      roles: "Subject",
      token: null,
      isOfflineMode: true,
      isDemoMode: true, // explicit flag, defaulting consistent with demo-first behavior
    },
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
    },
  }),
});
