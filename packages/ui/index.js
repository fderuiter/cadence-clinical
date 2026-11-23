export {
  debounce,
  createClinicalLookupInput,
  createConditionRowHTML,
  createRuleEditorHTML,
  serializeConditionsTree,
  deserializeConditionsTree,
  createSoaBuilderMatrix,
  createClinicalVisitMatrix,
  createSrOnlyActionButton,
} from "./src/legacyHtmlBuilders.js";

export {
  canonicalSerialize,
  generateCanonicalSignature,
  verifyCanonicalSignature,
  generateGatewaySignature,
  verifyGatewaySignature,
  generateJwtHS256,
  sha256,
  validateField,
  buildLedgerBlock,
  encryptAESGCM,
  decryptAESGCM,
  deriveSessionKey,
  deriveKeyFromPIN,
  SEMANTIC_ACTION_PATTERN,
  STEP_UP_ACTION_MAX_LENGTH,
  SEMANTIC_ACTIONS,
  isValidStepUpAction,
  isValidSemanticAction,
  buildStepUpVerificationPayload,
  SIGNATURE_OTHER_REASON,
  SIGNATURE_PASSWORD_INPUT_ATTRS,
  requiresCustomSignatureReason,
  resolveSignatureReason,
  deriveSignerRoleFromClaims,
} from "./signing.js";

export {
  DEFAULT_AUTH_RETURN_URL_KEY,
  sanitizeReturnUrl,
  saveAuthReturnUrl,
  peekAuthReturnUrl,
  consumeAuthReturnUrl,
  clearAuthReturnUrl,
  resolveOidcRedirectUri,
  isStepUpAuthFailure,
  createOidcSessionCoordinator,
} from "./session.js";

export {
  normalizeApprovedConsent,
  shapeComprehensionAnswers,
  interpretComprehensionResult,
} from "./econsent.js";

import {
  AiActionButton,
  AiSuggestionDrawer,
  AuditLogViewer,
  CLINICAL_ICON_MAP,
  CLINICAL_ICON_NAMES,
  CLINICAL_ICON_SIZES,
  CLINICAL_ICON_STROKE_WIDTH,
  ClinicalContextBar,
  ClinicalDataTable,
  ClinicalDrawer,
  ClinicalDropzone,
  ClinicalFieldLayout,
  ClinicalFormField,
  ClinicalIcon,
  ClinicalIconButton,
  ClinicalInput,
  ClinicalRadioGroup,
  ClinicalLookupInput,
  ClinicalModal,
  ClinicalQueryFlag,
  ClinicalQueryPanel,
  ConfidenceBadge,
  EmptyState,
  FileUploadModal,
  MediaPlayer,
  ModalErrorAlert,
  PersonaSwitcher,
  SignOutConfirmModal,
  enableExtendedClinicalIcons,
  enablePortalClinicalIcons,
  ensureExtendedClinicalIconsLoaded,
  isClinicalIconName,
  normalizeClinicalIconSize,
  renderClinicalIconSvg,
  useAsyncRetry,
  useFocusTrap,
  getFocusableElements,
  trapTabFocus,
  useEscapeClose,
  registerDraftSource,
  flushRegisteredDraftSources,
  flushRegisteredDraftSourcesSync,
  hasRegisteredUnsavedDrafts,
  resetDraftSourcesForTests,
  useSignOutGuard,
} from "./src/index.js";

export {
  AiActionButton,
  AiSuggestionDrawer,
  AuditLogViewer,
  CLINICAL_ICON_MAP,
  CLINICAL_ICON_NAMES,
  CLINICAL_ICON_SIZES,
  CLINICAL_ICON_STROKE_WIDTH,
  ClinicalContextBar,
  ClinicalDataTable,
  ClinicalDrawer,
  ClinicalDropzone,
  ClinicalFieldLayout,
  ClinicalFormField,
  ClinicalIcon,
  ClinicalIconButton,
  ClinicalInput,
  ClinicalRadioGroup,
  ClinicalLookupInput,
  ClinicalModal,
  ClinicalQueryFlag,
  ClinicalQueryPanel,
  ConfidenceBadge,
  EmptyState,
  FileUploadModal,
  MediaPlayer,
  ModalErrorAlert,
  PersonaSwitcher,
  SignOutConfirmModal,
  enableExtendedClinicalIcons,
  enablePortalClinicalIcons,
  ensureExtendedClinicalIconsLoaded,
  isClinicalIconName,
  normalizeClinicalIconSize,
  renderClinicalIconSvg,
  useAsyncRetry,
  useFocusTrap,
  getFocusableElements,
  trapTabFocus,
  useEscapeClose,
  registerDraftSource,
  flushRegisteredDraftSources,
  flushRegisteredDraftSourcesSync,
  hasRegisteredUnsavedDrafts,
  resetDraftSourcesForTests,
  useSignOutGuard,
};

// Dynamic Hover Pointer Capability Detection
export function initHoverDetection() {
  if (typeof window !== "undefined" && typeof document !== "undefined") {
    if (typeof window.matchMedia === "function") {
      const mediaQuery = window.matchMedia("(hover: hover)");
      const updateHoverClass = (e) => {
        if (e.matches) {
          document.body.classList.add("can-hover");
        } else {
          document.body.classList.remove("can-hover");
        }
      };

      updateHoverClass(mediaQuery);
      if (mediaQuery.addEventListener) {
        mediaQuery.addEventListener("change", updateHoverClass);
      } else if (mediaQuery.addListener) {
        mediaQuery.addListener(updateHoverClass);
      }
    } else {
      document.body.classList.add("can-hover");
    }
  }
}

initHoverDetection();
