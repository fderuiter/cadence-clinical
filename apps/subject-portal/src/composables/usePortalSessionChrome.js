import { DEFAULT_DEMO_PARTICIPANT } from "demo-fixtures";
import { computed } from "vue";

import { maskParticipantIdentifier } from "../features/site-support.js";
import { useSessionStore } from "../stores/session.js";
import { useUiStore } from "../stores/ui.js";
import { isDemoModeActive } from "../utils/demoMode.js";
import { usePortalViewport } from "./usePortalViewport.js";

export function usePortalSessionChrome() {
  const sessionStore = useSessionStore();
  const ui = useUiStore();
  const { isMobileViewport, isWideDesktopViewport } = usePortalViewport();

  const isDemo = computed(() => {
    void sessionStore.session?.isDemoMode;
    return isDemoModeActive();
  });

  const isSessionExpired = computed(
    () =>
      !sessionStore.session?.token &&
      !isDemo.value &&
      Boolean(sessionStore.session?.sessionExpired)
  );

  const hasSessionAlert = computed(
    () => Boolean(sessionStore.authError) || isSessionExpired.value
  );

  const showInlineSessionAlert = computed(
    () =>
      !isWideDesktopViewport.value &&
      !ui.sessionDrawerOpen &&
      hasSessionAlert.value
  );

  const maskedSubjectId = computed(() => {
    const raw = isDemo.value
      ? sessionStore.session?.userId || DEFAULT_DEMO_PARTICIPANT?.subjectCode
      : sessionStore.session?.userId;
    return maskParticipantIdentifier(raw) || "";
  });

  return {
    sessionStore,
    ui,
    isDemo,
    isSessionExpired,
    hasSessionAlert,
    showInlineSessionAlert,
    maskedSubjectId,
    isMobileViewport,
    isWideDesktopViewport,
  };
}
