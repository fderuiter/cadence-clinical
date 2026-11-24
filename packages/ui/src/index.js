import AiActionButton from "./components/clinical/AiActionButton.vue";
import AiSuggestionDrawer from "./components/clinical/AiSuggestionDrawer.vue";
import AuditLogViewer from "./components/clinical/AuditLogViewer.vue";
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
import ClinicalQueryFlag from "./components/clinical/ClinicalQueryFlag.vue";
import ClinicalQueryPanel from "./components/clinical/ClinicalQueryPanel.vue";
import ClinicalRadioGroup from "./components/clinical/ClinicalRadioGroup.vue";
import ClinicalTabs from "./components/clinical/ClinicalTabs.vue";
import ClinicalToastHost from "./components/clinical/ClinicalToastHost.vue";
import ConfidenceBadge from "./components/clinical/ConfidenceBadge.vue";
import EntityLink from "./components/clinical/EntityLink.vue";
import EntityPeekDrawer from "./components/clinical/EntityPeekDrawer.vue";
import {
  DIA_TMF_ARTIFACT_CATALOG,
  buildEntityBinderUrl,
  humanizeEntityStatus,
  normalizeEntitySubjectCode,
  resolveDiaArtifactMeta,
  resolveEntityLinkDescriptor,
} from "./components/clinical/entityLinkAdapter.js";
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
  getTopActiveModalContainer,
  hasActiveModalFocusTrap,
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

export {
  AiActionButton,
  AiSuggestionDrawer,
  AuditLogViewer,
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
  ClinicalQueryFlag,
  ClinicalQueryPanel,
  ClinicalRadioGroup,
  ClinicalTabs,
  ClinicalToastHost,
  ConfidenceBadge,
  DIA_TMF_ARTIFACT_CATALOG,
  EmptyState,
  EntityLink,
  EntityPeekDrawer,
  FileUploadModal,
  MediaPlayer,
  ModalErrorAlert,
  PersonaSwitcher,
  SignOutConfirmModal,
  buildEntityBinderUrl,
  flushRegisteredDraftSources,
  flushRegisteredDraftSourcesSync,
  getFocusableElements,
  getTopActiveModalContainer,
  handoffFocus,
  hasActiveModalFocusTrap,
  hasRegisteredUnsavedDrafts,
  humanizeEntityStatus,
  normalizeEntitySubjectCode,
  registerDraftSource,
  resetDraftSourcesForTests,
  resolveDiaArtifactMeta,
  resolveEntityLinkDescriptor,
  trapTabFocus,
  useAsyncRetry,
  useEscapeClose,
  useFocusTrap,
  useInlineFocusHandoff,
  useSignOutGuard,
};

export { registerEntityPeekHandler } from "./utils/entityPeekHandler.js";
export {
  parseNodeRef,
  stripRawPermissionTokens,
  openEntityPeek,
} from "./utils/entityPeekRegistry.js";
