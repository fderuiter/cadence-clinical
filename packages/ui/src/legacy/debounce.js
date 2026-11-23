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
