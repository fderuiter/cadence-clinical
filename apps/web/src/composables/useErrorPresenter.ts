/**
 * Error Presenter Composable
 * Translates backend API exceptions, stack traces, and microservice error dumps
 * into clean, user-friendly clinical messages while concealing internal
 * infrastructure topology and microservice service names from the UI.
 */

const MICROSERVICE_NAME_REPLACEMENTS: Array<[RegExp, string]> = [
  [/Designer service/gi, "protocol design service"],
  [/Designer/g, "Protocol Design"],
  [/econsent microservice/gi, "electronic consent service"],
  [/eConsent templates service/gi, "electronic consent service"],
  [/Execution service/gi, "study execution service"],
  [/backend microservice/gi, "clinical system backend"],
  [/REST backend/gi, "clinical system backend"],
];

const INFRASTRUCTURE_PATTERNS = [
  /http:\/\/[^\s]+/gi,
  /https:\/\/[^\s]+/gi,
  /\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}(:\d+)?\b/g,
  /at\s+[A-Za-z0-9_.]+\([^)]*\)/g, // Stack trace lines
  /\b(NpgsqlException|SqlException|NullReferenceException|InvalidOperationException|AxiosError)\b/g,
];

export function useErrorPresenter() {
  /**
   * Sanitizes a string error message by replacing microservice names and
   * stripping raw stack traces or internal URL topologies.
   */
  function sanitizeErrorText(rawText: string, fallbackMessage?: string): string {
    if (!rawText || typeof rawText !== "string") {
      return fallbackMessage || "An unexpected clinical system error occurred.";
    }

    let text = rawText.trim();

    // If text contains a raw stack trace or exception dump, extract first line or fallback
    if (text.includes("\n   at ") || text.includes("Exception:")) {
      const firstLine = text.split("\n")[0];
      if (firstLine && !firstLine.toLowerCase().includes("exception")) {
        text = firstLine;
      } else if (fallbackMessage) {
        return fallbackMessage;
      } else {
        text = "System service operation encountered an unhandled exception.";
      }
    }

    // Strip internal URL and IP topology references
    for (const pattern of INFRASTRUCTURE_PATTERNS) {
      text = text.replace(pattern, "").trim();
    }

    // Replace microservice names with clean clinical domain service names
    for (const [regex, replacement] of MICROSERVICE_NAME_REPLACEMENTS) {
      text = text.replace(regex, replacement);
    }

    // Clean up trailing colons or double spaces caused by regex replacements
    text = text.replace(/:\s*$/, "").replace(/\s{2,}/g, " ").trim();

    if (!text || text === ":") {
      return fallbackMessage || "System operation failed. Please try again.";
    }

    return text;
  }

  /**
   * Extracts error message from an unknown error object (e.g. Error, Axios response, string).
   */
  function extractRawMessage(error: unknown): string {
    if (!error) return "";
    if (typeof error === "string") return error;
    if (error instanceof Error) return error.message;

    const errObj = error as {
      response?: { data?: { detail?: string; message?: string; error?: string }; statusText?: string };
      message?: string;
      detail?: string;
    };

    if (errObj.response?.data?.detail) return errObj.response.data.detail;
    if (errObj.response?.data?.message) return errObj.response.data.message;
    if (errObj.response?.data?.error) return errObj.response.data.error;
    if (errObj.response?.statusText) return errObj.response.statusText;
    if (errObj.detail) return errObj.detail;
    if (errObj.message) return errObj.message;

    return String(error);
  }

  /**
   * Main error presenter method.
   * Logs full error details to console for audit/diagnostics,
   * then returns a sanitized, clinical-ready error message for the UI.
   */
  function presentError(error: unknown, fallbackMessage?: string): string {
    // Preserve diagnostic context for audit logs / debugging
    console.error("[useErrorPresenter Diagnostic Context]:", error);

    const raw = extractRawMessage(error);
    return sanitizeErrorText(raw, fallbackMessage);
  }

  /**
   * Alias for presentError.
   */
  function formatErrorMessage(error: unknown, fallbackMessage?: string): string {
    return presentError(error, fallbackMessage);
  }

  /**
   * Helper to sanitize text directly.
   */
  function sanitizeError(text: string, fallbackMessage?: string): string {
    return sanitizeErrorText(text, fallbackMessage);
  }

  return {
    presentError,
    formatErrorMessage,
    sanitizeError,
  };
}
