import {
  getCurrentInstance,
  onMounted,
  onUnmounted,
  unref,
  watch,
  type Ref,
} from "vue";
import type { RouteLocationNormalized, Router } from "vue-router";
import { registerDraftSource } from "ui";
import { confirmDialog } from "./useConfirmDialog.js";
import { shouldSuppressUnsavedNavigationGuard } from "./useRouteScopeSync";
import { useAuthStore } from "../stores/auth.js";

type MaybeFlag = boolean | Ref<boolean> | (() => boolean);

function resolveFlag(flag: MaybeFlag | undefined, fallback = false): boolean {
  if (flag === undefined) return fallback;
  return Boolean(typeof flag === "function" ? flag() : unref(flag));
}

export interface UnsavedNavigationDialogCopy {
  title: string;
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
}

export interface UseUnsavedNavigationGuardOptions {
  /** Reactive flag or getter indicating unsaved in-memory or draft edits. */
  isDirty: MaybeFlag;
  /** Whether route and browser unload guards are active (defaults to true). */
  guardNavigation?: MaybeFlag;
  /** Whether to register with useSignOutGuard so sign-out prompts before clearing state (defaults to true). */
  guardSignOut?: boolean;
  /** Optional flush callback for persisted drafts before sign-out check (omit for memory-only edits). */
  flushDrafts?: () => void;
  /** Accessible modal dialog copy or resolver for ConfirmationDialogHost. */
  dialogCopy:
    | UnsavedNavigationDialogCopy
    | ((
        to: RouteLocationNormalized,
        from: RouteLocationNormalized
      ) => UnsavedNavigationDialogCopy | null);
  /** Predicate deciding whether leaving to another route should prompt when dirty. */
  shouldPromptOnLeave?: (
    to: RouteLocationNormalized,
    from: RouteLocationNormalized
  ) => boolean;
  /** Predicate deciding whether updating the current route should prompt when dirty. */
  shouldPromptOnUpdate?: (
    to: RouteLocationNormalized,
    from: RouteLocationNormalized
  ) => boolean;
  /** Optional cleanup callback invoked after the user confirms discarding unsaved edits. */
  onConfirmedDiscard?: (
    to: RouteLocationNormalized,
    from: RouteLocationNormalized
  ) => void;
}

/**
 * Reusable dirty-state navigation guard across standalone views and Clinical Canvas workspaces (ADR-2671).
 *
 * Prompts via the accessible ConfirmationDialogHost modal (`role="alertdialog"`, never `window.confirm`),
 * blocks accidental browser tab unloads via `beforeunload`, integrates with `useSignOutGuard`, and stands down
 * automatically during involuntary session expiry, RBAC `/forbidden` redirects, or confirmed sign-out transitions.
 */
export function useUnsavedNavigationGuard(
  options: UseUnsavedNavigationGuardOptions
) {
  const instance = getCurrentInstance();
  const activeRouter = (instance?.appContext.config.globalProperties.$router ??
    null) as Router | null;
  let seenAuthenticated = false;
  try {
    const authStore = useAuthStore();
    seenAuthenticated = Boolean(authStore.isAuthenticated);
    watch(
      () => authStore.isAuthenticated,
      (authed) => {
        if (authed) {
          seenAuthenticated = true;
        }
      },
      { flush: "sync" }
    );
  } catch {
    seenAuthenticated = false;
  }

  function isGuardActive(): boolean {
    return resolveFlag(options.guardNavigation, true);
  }

  function shouldSuppressUnsavedGuard(to?: RouteLocationNormalized): boolean {
    try {
      const authStore = useAuthStore();
      if (authStore.isAuthenticated) {
        seenAuthenticated = true;
      }
      return shouldSuppressUnsavedNavigationGuard(
        {
          isAuthenticated: authStore.isAuthenticated || !seenAuthenticated,
          sessionExpired: authStore.sessionExpired,
          isLoggingOut: authStore.isLoggingOut,
        },
        to
      );
    } catch {
      return shouldSuppressUnsavedNavigationGuard(
        { isAuthenticated: true },
        to
      );
    }
  }

  async function confirmNavigation(
    to: RouteLocationNormalized,
    from: RouteLocationNormalized
  ): Promise<true | false> {
    const copy =
      typeof options.dialogCopy === "function"
        ? options.dialogCopy(to, from)
        : options.dialogCopy;
    if (!copy) return true;
    const confirmed = Boolean(
      await confirmDialog({
        title: copy.title,
        message: copy.message,
        confirmLabel: copy.confirmLabel ?? "Leave Without Saving",
        cancelLabel: copy.cancelLabel ?? "Stay",
        variant: "warning",
      })
    );
    if (confirmed) {
      options.onConfirmedDiscard?.(to, from);
      return true;
    }
    return false;
  }

  function handleBeforeUnload(event: BeforeUnloadEvent) {
    if (
      isGuardActive() &&
      resolveFlag(options.isDirty) &&
      !shouldSuppressUnsavedGuard()
    ) {
      event.preventDefault();
      event.returnValue = "";
      return "";
    }
  }

  if (
    activeRouter &&
    typeof activeRouter.beforeEach === "function" &&
    instance
  ) {
    const removeBeforeEach = activeRouter.beforeEach((to, from) => {
      if (
        !isGuardActive() ||
        !resolveFlag(options.isDirty) ||
        shouldSuppressUnsavedGuard(to)
      ) {
        return;
      }
      const sameRoute = Boolean(to.name && to.name === from.name);
      const shouldPrompt = sameRoute
        ? options.shouldPromptOnUpdate
          ? options.shouldPromptOnUpdate(to, from)
          : true
        : options.shouldPromptOnLeave
          ? options.shouldPromptOnLeave(to, from)
          : true;
      if (!shouldPrompt) return;
      return confirmNavigation(to, from).then((ok) => (ok ? undefined : false));
    });
    onUnmounted(() => {
      removeBeforeEach();
    });
  }

  let unregisterSignOut: (() => void) | null = null;
  if (options.guardSignOut !== false) {
    unregisterSignOut = registerDraftSource({
      flush: options.flushDrafts,
      hasDrafts: () => resolveFlag(options.isDirty),
    });
  }

  if (instance) {
    onMounted(() => {
      if (typeof window !== "undefined") {
        window.addEventListener("beforeunload", handleBeforeUnload);
      }
    });
    onUnmounted(() => {
      if (typeof window !== "undefined") {
        window.removeEventListener("beforeunload", handleBeforeUnload);
      }
      unregisterSignOut?.();
    });
  }

  return {
    handleBeforeUnload,
    shouldSuppressUnsavedGuard,
    unregisterSignOut,
  };
}
