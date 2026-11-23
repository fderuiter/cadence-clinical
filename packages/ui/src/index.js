import AiActionButton from "./components/clinical/AiActionButton.vue";
import AiSuggestionDrawer from "./components/clinical/AiSuggestionDrawer.vue";
import AuditLogViewer from "./components/clinical/AuditLogViewer.vue";
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
import ConfidenceBadge from "./components/clinical/ConfidenceBadge.vue";
import PersonaSwitcher from "./components/clinical/PersonaSwitcher.vue";
import SignOutConfirmModal from "./components/clinical/SignOutConfirmModal.vue";
import ThemeSwitcher from "./components/clinical/ThemeSwitcher.vue";
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
  flushRegisteredDraftSources,
  flushRegisteredDraftSourcesSync,
  hasRegisteredUnsavedDrafts,
  registerDraftSource,
  resetDraftSourcesForTests,
  useSignOutGuard,
} from "./composables/useSignOutGuard.js";
import {
  CADENCE_THEME_STORAGE_KEY,
  THEME_MODES,
  VITEPRESS_THEME_STORAGE_KEY,
  applyThemeToDocument,
  cycleThemePreference,
  getResolvedTheme,
  getThemePreference,
  initTheme,
  normalizeThemePreference,
  readStoredThemePreference,
  resetThemeControllerForTests,
  resolveEffectiveTheme,
  setThemePreference,
  useTheme,
} from "./composables/useTheme.js";

export {
  AiActionButton,
  AiSuggestionDrawer,
  AuditLogViewer,
  CADENCE_THEME_STORAGE_KEY,
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
  ConfidenceBadge,
  EmptyState,
  FileUploadModal,
  MediaPlayer,
  ModalErrorAlert,
  PersonaSwitcher,
  SignOutConfirmModal,
  THEME_MODES,
  ThemeSwitcher,
  VITEPRESS_THEME_STORAGE_KEY,
  applyThemeToDocument,
  cycleThemePreference,
  flushRegisteredDraftSources,
  flushRegisteredDraftSourcesSync,
  getFocusableElements,
  getResolvedTheme,
  getThemePreference,
  hasRegisteredUnsavedDrafts,
  initTheme,
  normalizeThemePreference,
  readStoredThemePreference,
  registerDraftSource,
  resetDraftSourcesForTests,
  resetThemeControllerForTests,
  resolveEffectiveTheme,
  setThemePreference,
  trapTabFocus,
  useAsyncRetry,
  useEscapeClose,
  useFocusTrap,
  useSignOutGuard,
  useTheme,
};
