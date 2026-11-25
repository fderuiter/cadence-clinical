import ClinicalDrawer from "./src/components/clinical/ClinicalDrawer.vue";
import ClinicalDropzone from "./src/components/clinical/ClinicalDropzone.vue";
import ClinicalIcon from "./src/components/clinical/ClinicalIcon.vue";
import ClinicalModal from "./src/components/clinical/ClinicalModal.vue";
import EntityLink from "./src/components/clinical/EntityLink.vue";
import SignOutConfirmModal from "./src/components/clinical/SignOutConfirmModal.vue";
import FileUploadModal from "./src/components/FileUploadModal.vue";
import ModalErrorAlert from "./src/components/ModalErrorAlert.vue";
import { useEscapeClose } from "./src/composables/useEscapeClose.js";
import {
  getFocusableElements,
  hasActiveModalFocusTrap,
  trapTabFocus,
  useFocusTrap,
} from "./src/composables/useFocusTrap.js";
import {
  handoffFocus,
  handoffRetryFocus,
} from "./src/composables/useInlineFocusHandoff.js";
import { useSignOutGuard } from "./src/composables/useSignOutGuard.js";

export {
  ClinicalDrawer,
  ClinicalDropzone,
  ClinicalIcon,
  ClinicalModal,
  EntityLink,
  FileUploadModal,
  ModalErrorAlert,
  SignOutConfirmModal,
  getFocusableElements,
  handoffFocus,
  handoffRetryFocus,
  hasActiveModalFocusTrap,
  trapTabFocus,
  useEscapeClose,
  useFocusTrap,
  useSignOutGuard,
};
