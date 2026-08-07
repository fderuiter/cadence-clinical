/**
 * Reusable Vue directive to handle keyboard activation (Space & Enter) on clickable elements.
 * Automatically applies appropriate interactive roles (role="button"), tabindex="0", and keyboard listeners.
 * Prevents triggering if focus is on form inputs, buttons, textareas, etc.
 */
export const vKeyboardClick = {
  mounted(el, binding) {
    // 1. Automatically apply tabindex if not already set
    if (!el.hasAttribute("tabindex")) {
      el.setAttribute("tabindex", "0");
    }

    // 2. Automatically apply role="button" if not a native interactive element
    const tagName = el.tagName;
    if (
      !el.hasAttribute("role") &&
      tagName !== "BUTTON" &&
      tagName !== "INPUT" &&
      tagName !== "SELECT" &&
      tagName !== "A" &&
      tagName !== "TEXTAREA"
    ) {
      el.setAttribute("role", "button");
    }

    // 3. Store reference to the callback on the element to keep it reactive and updated
    el._keyboardClickCallback = binding.value;

    // 4. Keyboard keydown handler
    el._keyboardClickHandler = (e) => {
      if (e.key === "Enter" || e.key === " ") {
        const targetTag = e.target.tagName;
        // Do not intercept if focus is inside a form input element
        if (
          targetTag === "INPUT" ||
          targetTag === "TEXTAREA" ||
          targetTag === "SELECT" ||
          targetTag === "BUTTON" ||
          e.target.isContentEditable
        ) {
          return;
        }
        e.preventDefault();

        if (typeof el._keyboardClickCallback === "function") {
          el._keyboardClickCallback(e);
        } else {
          el.click();
        }
      }
    };

    el.addEventListener("keydown", el._keyboardClickHandler);
  },

  updated(el, binding) {
    el._keyboardClickCallback = binding.value;
  },

  unmounted(el) {
    if (el._keyboardClickHandler) {
      el.removeEventListener("keydown", el._keyboardClickHandler);
      delete el._keyboardClickHandler;
    }
    delete el._keyboardClickCallback;
  },
};
