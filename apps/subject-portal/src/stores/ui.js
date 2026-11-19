// Shell state: the active view (kept in step with the router), the signature
// modal's pending action and focus bookkeeping. Keys the other stores do not
// own are also kept here by the `state` facade.
import { defineStore } from "pinia";

export const useUiStore = defineStore("ui", {
  state: () => ({
    currentView: "view-tasks",
    unavailableScreen: null,
    pendingSignableAction: null,
    modalError: "",
    previouslyFocusedElement: null,
    initiatingButton: null,
    draftPresence: {},
    questionnaireCancelConfirmOpen: false,
    siteSupportContact: null,
  }),
});
