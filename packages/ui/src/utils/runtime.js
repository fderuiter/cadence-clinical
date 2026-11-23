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

/**
 * Dynamic Hover Pointer Capability Detection
 */
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
