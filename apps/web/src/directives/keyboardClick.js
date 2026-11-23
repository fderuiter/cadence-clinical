/**
 * Reusable Vue directive to handle keyboard activation (Space & Enter) on clickable elements.
 * Applies role="button" and tabindex="0" only when the host is neither a native control nor a
 * container with interactive descendants (WCAG 2.2 SC 4.1.2, Failure F59).
 */
const INTERACTIVE_SELECTOR =
  'button,a[href],input,select,textarea,summary,[contenteditable="true"],[role="button"],[role="checkbox"],[role="link"],[role="menuitem"],[role="option"],[role="radio"],[role="switch"],[role="tab"]';
const NATIVE_TAG_RE = /^(BUTTON|INPUT|SELECT|A|TEXTAREA|SUMMARY)$/;

function syncSemantics(el) {
  if (el?.querySelector?.(INTERACTIVE_SELECTOR)) {
    if (el._kcR) el.removeAttribute("role");
    if (el._kcT) el.removeAttribute("tabindex");
    el._kcR = el._kcT = false;
    return;
  }
  if (!el.hasAttribute("tabindex")) {
    el.setAttribute("tabindex", "0");
    el._kcT = true;
  }
  if (!el.hasAttribute("role") && !NATIVE_TAG_RE.test(el.tagName)) {
    el.setAttribute("role", "button");
    el._kcR = true;
  }
}

export const vKeyboardClick = {
  mounted(el, binding) {
    syncSemantics(el);
    el._kcCb = binding.value;
    el._kcFn = (e) => {
      const t = e.target;
      if (
        (e.key !== "Enter" && e.key !== " ") ||
        el?.querySelector?.(INTERACTIVE_SELECTOR) ||
        NATIVE_TAG_RE.test(t?.tagName) ||
        t?.isContentEditable ||
        (t && t !== el && t.closest?.(INTERACTIVE_SELECTOR))
      ) {
        return;
      }
      e.preventDefault();
      if (typeof el._kcCb === "function") el._kcCb(e);
      else el.click();
    };
    el.addEventListener("keydown", el._kcFn);
  },

  updated(el, binding) {
    el._kcCb = binding.value;
    syncSemantics(el);
  },

  unmounted(el) {
    if (el._kcFn) {
      el.removeEventListener("keydown", el._kcFn);
      delete el._kcFn;
      delete el._kcCb;
    }
  },
};
