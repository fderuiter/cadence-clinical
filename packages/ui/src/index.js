import ClinicalBreadcrumbs from "./components/clinical/ClinicalBreadcrumbs.vue";
import ClinicalContextBar from "./components/clinical/ClinicalContextBar.vue";
import ClinicalDataTable from "./components/clinical/ClinicalDataTable.vue";
import ClinicalDrawer from "./components/clinical/ClinicalDrawer.vue";
import ClinicalDropzone from "./components/clinical/ClinicalDropzone.vue";
import ClinicalExplanation from "./components/clinical/ClinicalExplanation.vue";
import ClinicalFieldLayout from "./components/clinical/ClinicalFieldLayout.vue";
import ClinicalFormField from "./components/clinical/ClinicalFormField.vue";
import ClinicalGatedAction from "./components/clinical/ClinicalGatedAction.vue";
import ClinicalIcon from "./components/clinical/ClinicalIcon.vue";
import ClinicalIconButton from "./components/clinical/ClinicalIconButton.vue";
import ClinicalInput from "./components/clinical/ClinicalInput.vue";
import ClinicalLookupInput from "./components/clinical/ClinicalLookupInput.vue";
import ClinicalModal from "./components/clinical/ClinicalModal.vue";
import ClinicalPopover from "./components/clinical/ClinicalPopover.vue";
import ClinicalQueryFlag from "./components/clinical/ClinicalQueryFlag.vue";
import ClinicalQueryPanel from "./components/clinical/ClinicalQueryPanel.vue";
import ClinicalRadioGroup from "./components/clinical/ClinicalRadioGroup.vue";
import ClinicalRecordCard from "./components/clinical/ClinicalRecordCard.vue";
import ClinicalTabs from "./components/clinical/ClinicalTabs.vue";
import ClinicalThemeToggle from "./components/clinical/ClinicalThemeToggle.vue";
import ClinicalToastHost from "./components/clinical/ClinicalToastHost.vue";
import EntityLink from "./components/clinical/EntityLink.vue";
import SignOutConfirmModal from "./components/clinical/SignOutConfirmModal.vue";
import EmptyState from "./components/common/EmptyState.vue";
import MediaPlayer from "./components/MediaPlayer.vue";
import ModalErrorAlert from "./components/ModalErrorAlert.vue";
import { use2DGrid } from "./composables/use2DGrid.js";
import { useEscapeClose } from "./composables/useEscapeClose.js";
import {
  useFloatingPosition,
  useViewportMaxHeight,
} from "./composables/useFloatingPosition.js";
import {
  computeFloatingPosition,
  getVisualViewportBounds,
  observeVisualViewport,
} from "./utils/floatingGeometry.js";
import {
  getFocusableElements,
  getTopActiveModalContainer,
  hasActiveModalFocusTrap,
  trapTabFocus,
  useFocusTrap,
} from "./composables/useFocusTrap.js";
import {
  handoffFocus,
  handoffRetryFocus,
  useInlineFocusHandoff,
  useRetryFocusHandoff,
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
  PREFERS_DARK_MEDIA_QUERY,
  THEME_CHANGE_EVENT,
  THEME_MODES,
  THEME_STORAGE_KEY,
  applyThemeToDocument,
  bootstrapTheme,
  cycleThemePreference,
  detectSystemDarkPreference,
  normalizeThemePreference,
  readStoredThemePreference,
  resolveEffectiveTheme,
  subscribeThemeSync,
  useTheme,
  writeStoredThemePreference,
} from "./composables/useTheme.js";

export {
  ClinicalBreadcrumbs,
  ClinicalContextBar,
  ClinicalDataTable,
  ClinicalDrawer,
  ClinicalDropzone,
  ClinicalExplanation,
  ClinicalFieldLayout,
  ClinicalFormField,
  ClinicalGatedAction,
  ClinicalIcon,
  ClinicalIconButton,
  ClinicalInput,
  ClinicalLookupInput,
  ClinicalModal,
  ClinicalPopover,
  ClinicalQueryFlag,
  ClinicalQueryPanel,
  ClinicalRadioGroup,
  ClinicalRecordCard,
  ClinicalTabs,
  ClinicalThemeToggle,
  ClinicalToastHost,
  EmptyState,
  EntityLink,
  MediaPlayer,
  ModalErrorAlert,
  PREFERS_DARK_MEDIA_QUERY,
  SignOutConfirmModal,
  THEME_CHANGE_EVENT,
  THEME_MODES,
  THEME_STORAGE_KEY,
  applyThemeToDocument,
  bootstrapTheme,
  computeFloatingPosition,
  cycleThemePreference,
  detectSystemDarkPreference,
  flushRegisteredDraftSources,
  flushRegisteredDraftSourcesSync,
  getFocusableElements,
  getTopActiveModalContainer,
  getVisualViewportBounds,
  handoffFocus,
  handoffRetryFocus,
  hasActiveModalFocusTrap,
  hasRegisteredUnsavedDrafts,
  normalizeThemePreference,
  observeVisualViewport,
  readStoredThemePreference,
  registerDraftSource,
  resetDraftSourcesForTests,
  resolveEffectiveTheme,
  subscribeThemeSync,
  trapTabFocus,
  use2DGrid,
  useEscapeClose,
  useFloatingPosition,
  useFocusTrap,
  useInlineFocusHandoff,
  useRetryFocusHandoff,
  useSignOutGuard,
  useTheme,
  useViewportMaxHeight,
  writeStoredThemePreference,
};

export { registerEntityPeekHandler } from "./utils/entityPeekHandler.js";
export {
  parseNodeRef,
  stripRawPermissionTokens,
  openEntityPeek,
} from "./utils/entityPeekRegistry.js";
export {
  CLINICAL_SHELL_ICON_NAMES,
  CLINICAL_ROUTE_ICON_NAMES,
  CLINICAL_ICON_NAMES,
  CLINICAL_ICON_STROKE_WIDTH,
  isClinicalShellIconName,
  isClinicalRouteIconName,
  isClinicalIconName,
  normalizeClinicalIconSize,
} from "./icons/iconNames.js";
export { CLINICAL_SHELL_ICONS } from "./icons/shellIcons.js";
export {
  CLINICAL_ROUTE_ICONS,
  CLINICAL_ICONS,
  resolveClinicalIcon,
} from "./icons/clinicalIcons.js";
export { renderClinicalIconSvg } from "./icons/renderClinicalIconSvg.js";
