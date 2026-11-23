import AiActionButton from "./components/clinical/AiActionButton.vue";
import AiSuggestionDrawer from "./components/clinical/AiSuggestionDrawer.vue";
import AuditLogViewer from "./components/clinical/AuditLogViewer.vue";
import ClinicalContextBar from "./components/clinical/ClinicalContextBar.vue";
import ClinicalDataTable from "./components/clinical/ClinicalDataTable.vue";
import ClinicalDrawer from "./components/clinical/ClinicalDrawer.vue";
import ClinicalDropzone from "./components/clinical/ClinicalDropzone.vue";
import ClinicalFieldLayout from "./components/clinical/ClinicalFieldLayout.vue";
import ClinicalFormField from "./components/clinical/ClinicalFormField.vue";
import ClinicalIconButton from "./components/clinical/ClinicalIconButton.vue";
import ClinicalInput from "./components/clinical/ClinicalInput.vue";
import ClinicalLookupInput from "./components/clinical/ClinicalLookupInput.vue";
import ClinicalModal from "./components/clinical/ClinicalModal.vue";
import ClinicalQueryFlag from "./components/clinical/ClinicalQueryFlag.vue";
import ClinicalQueryPanel from "./components/clinical/ClinicalQueryPanel.vue";
import ClinicalRadioGroup from "./components/clinical/ClinicalRadioGroup.vue";
import ClinicalSlider from "./components/clinical/ClinicalSlider.vue";
import ClinicalTabs from "./components/clinical/ClinicalTabs.vue";
import ConfidenceBadge from "./components/clinical/ConfidenceBadge.vue";
import PersonaSwitcher from "./components/clinical/PersonaSwitcher.vue";
import SignOutConfirmModal from "./components/clinical/SignOutConfirmModal.vue";
import EmptyState from "./components/common/EmptyState.vue";
import FileUploadModal from "./components/FileUploadModal.vue";
import MediaPlayer from "./components/MediaPlayer.vue";
import ModalErrorAlert from "./components/ModalErrorAlert.vue";
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
import {
  CADENCE_THEME_KEY,
  SURFACE_THEME_EVENT,
  THEME_APPEARANCE_KEY,
  applySurfaceTheme,
  initSurfaceThemeSync,
  isEffectiveDarkTheme,
  resolveSurfaceTheme,
  setSurfaceTheme,
  toggleSurfaceTheme,
} from "./composables/useSurfaceTheme.js";
import { debounce, initHoverDetection } from "./utils/runtime.js";

export {
  AiActionButton,
  AiSuggestionDrawer,
  AuditLogViewer,
  CADENCE_THEME_KEY,
  ClinicalContextBar,
  ClinicalDataTable,
  ClinicalDrawer,
  ClinicalDropzone,
  ClinicalFieldLayout,
  ClinicalFormField,
  ClinicalIconButton,
  ClinicalInput,
  ClinicalLookupInput,
  ClinicalModal,
  ClinicalQueryFlag,
  ClinicalQueryPanel,
  ClinicalRadioGroup,
  ClinicalSlider,
  ClinicalTabs,
  ConfidenceBadge,
  EmptyState,
  FileUploadModal,
  MediaPlayer,
  ModalErrorAlert,
  PersonaSwitcher,
  SURFACE_THEME_EVENT,
  SignOutConfirmModal,
  THEME_APPEARANCE_KEY,
  applySurfaceTheme,
  debounce,
  flushRegisteredDraftSources,
  flushRegisteredDraftSourcesSync,
  getFocusableElements,
  handoffFocus,
  hasRegisteredUnsavedDrafts,
  initHoverDetection,
  initSurfaceThemeSync,
  isEffectiveDarkTheme,
  registerDraftSource,
  resetDraftSourcesForTests,
  resolveSurfaceTheme,
  setSurfaceTheme,
  toggleSurfaceTheme,
  trapTabFocus,
  useAsyncRetry,
  useEscapeClose,
  useFocusTrap,
  useInlineFocusHandoff,
  useSignOutGuard,
};
