/**
 * A small debounce utility that limits function execution during rapid invocation.
 *
 * @param {Function} func - The function to debounce.
 * @param {number} wait - The delay in milliseconds before executing the function.
 * @returns {Function} The debounced function.
 */
export function debounce(func, wait) {
  let timeout = null;
  let pendingArgs = null;
  let pendingContext = null;

  function debounced(...args) {
    pendingContext = this;
    pendingArgs = args;
    if (timeout !== null) {
      clearTimeout(timeout);
    }
    timeout = setTimeout(() => {
      const ctx = pendingContext;
      const callArgs = pendingArgs;
      timeout = null;
      pendingContext = null;
      pendingArgs = null;
      func.apply(ctx, callArgs);
    }, wait);
  }

  debounced.flush = function flushDebounced() {
    if (timeout === null) {
      return undefined;
    }
    clearTimeout(timeout);
    const ctx = pendingContext;
    const callArgs = pendingArgs;
    timeout = null;
    pendingContext = null;
    pendingArgs = null;
    return func.apply(ctx, callArgs);
  };

  debounced.cancel = function cancelDebounced() {
    if (timeout !== null) {
      clearTimeout(timeout);
      timeout = null;
    }
    pendingContext = null;
    pendingArgs = null;
  };

  debounced.pending = function isDebouncedPending() {
    return timeout !== null;
  };

  return debounced;
}

export {
  createClinicalLookupInput,
  createConditionRowHTML,
  createRuleEditorHTML,
  serializeConditionsTree,
  deserializeConditionsTree,
  createSoaBuilderMatrix,
  createClinicalVisitMatrix,
  createSrOnlyActionButton,
} from "./src/builders.js";

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
  parseClaimStringList,
  decodeJwtPayloadClaims,
  extractAssignedStudies,
  extractAssignedSites,
  extractPrimaryStudyId,
  extractPrimarySiteId,
  isForbiddenScopeError,
  formatScopeForbiddenGuidance,
} from "./callerScope.js";

export {
  normalizeApprovedConsent,
  shapeComprehensionAnswers,
  interpretComprehensionResult,
} from "./econsent.js";

import {
  AiActionButton,
  AiSuggestionDrawer,
  AuditLogViewer,
  ClinicalContextBar,
  ClinicalDataTable,
  ClinicalDrawer,
  ClinicalDropzone,
  ClinicalExplanation,
  ClinicalFieldLayout,
  ClinicalFormField,
  ClinicalGatedAction,
  ClinicalIconButton,
  ClinicalIcon,
  ClinicalBreadcrumbs,
  EntityLink,
  ClinicalInput,
  ClinicalRadioGroup,
  ClinicalTabs,
  ClinicalLookupInput,
  ClinicalModal,
  ClinicalQueryFlag,
  ClinicalQueryPanel,
  ClinicalToastHost,
  ConfidenceBadge,
  EmptyState,
  FileUploadModal,
  MediaPlayer,
  ModalErrorAlert,
  PersonaSwitcher,
  SignOutConfirmModal,
  useAsyncRetry,
  useFocusTrap,
  getFocusableElements,
  getTopActiveModalContainer,
  hasActiveModalFocusTrap,
  trapTabFocus,
  handoffFocus,
  useInlineFocusHandoff,
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
  ClinicalRadioGroup,
  ClinicalTabs,
  ClinicalLookupInput,
  ClinicalModal,
  ClinicalQueryFlag,
  ClinicalQueryPanel,
  ClinicalToastHost,
  ConfidenceBadge,
  EmptyState,
  EntityLink,
  FileUploadModal,
  MediaPlayer,
  ModalErrorAlert,
  PersonaSwitcher,
  SignOutConfirmModal,
  useAsyncRetry,
  useFocusTrap,
  getFocusableElements,
  getTopActiveModalContainer,
  hasActiveModalFocusTrap,
  trapTabFocus,
  handoffFocus,
  useInlineFocusHandoff,
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

export {
  openEntityPeek,
  parseNodeRef,
  registerEntityPeekHandler,
  stripRawPermissionTokens,
} from "./src/index.js";
