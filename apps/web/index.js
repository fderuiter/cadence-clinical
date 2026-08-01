import { evaluateAST } from "./src/evaluator.js";
import { validateField as uiValidateField } from "ui";

/**
 * Pure evaluation function for dynamic forms.
 * Determines field visibility, performs cascading dependent nullification,
 * and tracks field values and audit logs.
 *
 * @param {Array} fields - Array of field metadata objects
 * @param {Object} currentValues - Current state of form values
 * @param {Function} logPurge - Callback function to log field purges in the audit trail
 * @returns {Object} { visibleFields, updatedValues }
 */
export function renderFormFromJSON(fields, currentValues, logPurge = null) {
  const updatedValues = { ...currentValues };
  const visibleFields = {};

  let changed = true;
  let passes = 0;
  // Support cascading up to 10 levels deep
  while (changed && passes < 10) {
    changed = false;
    passes++;

    for (const field of fields) {
      const isRelevant = field.relevant
        ? evaluateAST(field.relevant, updatedValues) !== false
        : true;

      const wasVisible = visibleFields[field.id] !== false;
      if (visibleFields[field.id] === undefined || isRelevant !== wasVisible) {
        visibleFields[field.id] = isRelevant;
        changed = true;
      }

      if (!isRelevant) {
        const val = updatedValues[field.id];
        if (val !== undefined && val !== "" && val !== null) {
          updatedValues[field.id] = "";
          if (logPurge) {
            logPurge(
              field.id,
              val,
              "System-initiated purge of inactive child variable due to parent value mutation"
            );
          }
          changed = true;
        }
      }
    }
  }

  return { visibleFields, updatedValues };
}

/**
 * Wrapper for field validation that supports AST constraints.
 *
 * @param {Object} fieldMeta - Field metadata configuration
 * @param {any} val - Field value
 * @param {Object} context - Optional form context for constraint evaluation
 * @returns {Object} { valid: boolean, message: string }
 */
export function validateField(fieldMeta, val, context = {}) {
  return uiValidateField(fieldMeta, val, context, evaluateAST);
}
