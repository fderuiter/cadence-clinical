export { debounce } from "./src/utils/debounce.js";

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
  formatCgtBedsideBindingId,
  formatCgtDestructionBindingId,
  STEP_UP_VERIFICATION_PATH,
  resolveDefaultSemanticAction,
  requestStepUpSignature,
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

export * from "./src/index.js";

import { initHoverDetection } from "./hover.js";
export { initHoverDetection };

initHoverDetection();
