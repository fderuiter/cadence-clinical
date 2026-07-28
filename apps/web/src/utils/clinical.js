import {
  validateField as sharedValidateField,
  sha256 as sharedSha256,
  buildLedgerBlock as sharedBuildLedgerBlock,
  generateCanonicalSignature as sharedGenerateCanonicalSignature,
  verifyCanonicalSignature as sharedVerifyCanonicalSignature,
  generateGatewaySignature as sharedGenerateGatewaySignature,
  verifyGatewaySignature as sharedVerifyGatewaySignature,
  generateJwtHS256 as sharedGenerateJwtHS256,
  canonicalSerialize as sharedCanonicalSerialize,
} from "ui";
import { evaluateAST } from "../evaluator.js";

/**
 * Modern Clinical Utility Module for Cadence Clinical SPA.
 * Wraps shared workspace validators and wires the AST evaluator.
 */

export function validateField(fieldMeta, val, context = {}) {
  return sharedValidateField(fieldMeta, val, context, evaluateAST);
}

export const sha256 = sharedSha256;
export const buildLedgerBlock = sharedBuildLedgerBlock;
export const generateCanonicalSignature = sharedGenerateCanonicalSignature;
export const verifyCanonicalSignature = sharedVerifyCanonicalSignature;
export const generateGatewaySignature = sharedGenerateGatewaySignature;
export const verifyGatewaySignature = sharedVerifyGatewaySignature;
export const generateJwtHS256 = sharedGenerateJwtHS256;
export const canonicalSerialize = sharedCanonicalSerialize;
