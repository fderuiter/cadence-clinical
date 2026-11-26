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
import ClinicalTabs from "./components/clinical/ClinicalTabs.vue";
import ClinicalToastHost from "./components/clinical/ClinicalToastHost.vue";
import EntityLink from "./components/clinical/EntityLink.vue";
import SignOutConfirmModal from "./components/clinical/SignOutConfirmModal.vue";
import EmptyState from "./components/common/EmptyState.vue";
import MediaPlayer from "./components/MediaPlayer.vue";
import ModalErrorAlert from "./components/ModalErrorAlert.vue";
import { useEscapeClose } from "./composables/useEscapeClose.js";
import {
  computeFloatingPosition,
  getVisualViewportBounds,
  observeVisualViewport,
  useFloatingPosition,
  useViewportMaxHeight,
} from "./composables/useFloatingPosition.js";
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
  ClinicalTabs,
  ClinicalToastHost,
  EmptyState,
  EntityLink,
  MediaPlayer,
  ModalErrorAlert,
  SignOutConfirmModal,
  computeFloatingPosition,
  flushRegisteredDraftSources,
  flushRegisteredDraftSourcesSync,
  getFocusableElements,
  getTopActiveModalContainer,
  getVisualViewportBounds,
  handoffFocus,
  handoffRetryFocus,
  hasActiveModalFocusTrap,
  hasRegisteredUnsavedDrafts,
  observeVisualViewport,
  registerDraftSource,
  resetDraftSourcesForTests,
  trapTabFocus,
  useEscapeClose,
  useFloatingPosition,
  useFocusTrap,
  useInlineFocusHandoff,
  useRetryFocusHandoff,
  useSignOutGuard,
  useViewportMaxHeight,
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
