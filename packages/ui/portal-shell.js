import ClinicalBreadcrumbs from "./src/components/clinical/ClinicalBreadcrumbs.vue";
import ClinicalIcon from "./src/components/clinical/ClinicalIcon.vue";
import ClinicalToastHost from "./src/components/clinical/ClinicalToastHost.vue";
import { handoffFocus } from "./src/composables/useInlineFocusHandoff.js";
import { defineLazyOverlay } from "./src/composables/useLazyOverlay.js";
import {
  flushRegisteredDraftSources,
  hasRegisteredUnsavedDrafts,
  registerDraftSource,
  useSignOutGuard,
} from "./src/composables/useSignOutGuard.js";
import { debounce } from "./src/utils/debounce.js";
import { initHoverDetection } from "./hover.js";

export {
  ClinicalBreadcrumbs,
  ClinicalIcon,
  ClinicalToastHost,
  debounce,
  defineLazyOverlay,
  flushRegisteredDraftSources,
  handoffFocus,
  hasRegisteredUnsavedDrafts,
  initHoverDetection,
  registerDraftSource,
  useSignOutGuard,
};
