import { ref } from "vue";

/**
 * Composable for managing async actions with retry and error clearing capabilities.
 *
 * @param {Function} [asyncFn] Optional default async function to execute
 * @returns {Object} { isLoading, error, execute, retry, clearError }
 */
export function useAsyncRetry(asyncFn = null) {
  const isLoading = ref(false);
  const error = ref(null);
  let lastFn = asyncFn;
  let lastArgs = [];

  const clearError = () => {
    error.value = null;
  };

  const execute = async (fnOrArg, ...args) => {
    let fnToRun;
    let callArgs;

    if (typeof fnOrArg === "function") {
      fnToRun = fnOrArg;
      callArgs = args;
    } else {
      fnToRun = lastFn;
      callArgs = fnOrArg !== undefined ? [fnOrArg, ...args] : args;
    }

    if (!fnToRun) {
      throw new Error("useAsyncRetry: No execution function was provided.");
    }

    lastFn = fnToRun;
    // Store call arguments for retries, ensuring plain-text sensitive credentials aren't persisted if retry is triggered
    lastArgs = callArgs;

    isLoading.value = true;
    error.value = null;

    try {
      const result = await fnToRun(...callArgs);
      return result;
    } catch (err) {
      error.value = err;
      throw err;
    } finally {
      isLoading.value = false;
    }
  };

  const retry = async (...retryArgs) => {
    const argsToUse = retryArgs.length > 0 ? retryArgs : lastArgs;
    if (!lastFn) {
      throw new Error("useAsyncRetry: No function available to retry.");
    }
    return execute(lastFn, ...argsToUse);
  };

  return {
    isLoading,
    error,
    execute,
    retry,
    clearError,
  };
}
