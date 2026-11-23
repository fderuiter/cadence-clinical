import { useAsyncRetry } from "./composables/useAsyncRetry.js";
import { useEscapeClose } from "./composables/useEscapeClose.js";
import {
  getFocusableElements,
  trapTabFocus,
  useFocusTrap,
} from "./composables/useFocusTrap.js";
import {
  handoffFocus,
  useInlineFocusHandoff,
} from "./composables/useInlineFocusHandoff.js";
import {
  flushRegisteredDraftSources,
  flushRegisteredDraftSourcesSync,
  hasRegisteredUnsavedDrafts,
  registerDraftSource,
  resetDraftSourcesForTests,
  useSignOutGuard,
} from "./composables/useSignOutGuard.js";

const componentModules = import.meta.glob("./components/**/*.vue", {
  eager: true,
});

export const UI_COMPONENTS = Object.freeze(
  Object.fromEntries(
    Object.entries(componentModules)
      .map(([filePath, mod]) => {
        const fileName = filePath.split("/").pop() || "";
        const componentName = fileName.replace(/\.vue$/, "");
        return [componentName, mod.default];
      })
      .sort(([left], [right]) => (left === right ? 0 : left < right ? -1 : 1))
  )
);

// Named component re-exports are injected at build/test/serve time by createUiAutoBarrelPlugin
// so adding a .vue file under ./components/ requires zero edits to tracked barrel files.
export {
  flushRegisteredDraftSources,
  flushRegisteredDraftSourcesSync,
  getFocusableElements,
  handoffFocus,
  hasRegisteredUnsavedDrafts,
  registerDraftSource,
  resetDraftSourcesForTests,
  trapTabFocus,
  useAsyncRetry,
  useEscapeClose,
  useFocusTrap,
  useInlineFocusHandoff,
  useSignOutGuard,
};
