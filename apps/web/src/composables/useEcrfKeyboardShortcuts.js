import { ref, onMounted, onUnmounted, toValue } from "vue";

/**
 * Vue composable for eCRF Keyboard Shortcuts and Auto-Advance Focus Engine.
 * Provides hotkey handling for EDC clinical data entry and CRA monitoring.
 *
 * Shortcuts:
 * - Enter: Advance focus to next visible eCRF input field
 * - Ctrl+S / Cmd+S / Alt+S: Save and submit current eCRF form
 * - Alt+Q: Toggle query creation / response drawer for active field
 * - Alt+N / Alt+P: Navigate focus between fields with active queries
 * - Alt+V: Toggle SDV verification status for active field (CRA Monitors only)
 * - ? or Ctrl+/: Open keyboard shortcut cheat sheet overlay
 */
export function useEcrfKeyboardShortcuts(options = {}) {
  const {
    onSubmit = null,
    isCraUser = false,
    activeUserRole = "site_investigator",
    isModalActive = false,
    formContainerRef = null,
  } = options;

  const showHotkeyOverlay = ref(false);

  function toggleHotkeyOverlay() {
    showHotkeyOverlay.value = !showHotkeyOverlay.value;
  }

  function openHotkeyOverlay() {
    showHotkeyOverlay.value = true;
  }

  function closeHotkeyOverlay() {
    showHotkeyOverlay.value = false;
  }

  // Helper to resolve ref, getter function, or value
  function resolveVal(valOrFn) {
    return toValue(valOrFn);
  }

  function getIsCra() {
    const craVal = resolveVal(isCraUser);
    if (craVal) return true;
    const role = resolveVal(activeUserRole);
    return role === "cra" || role === "monitor";
  }

  function getIsModalOpen() {
    return Boolean(resolveVal(isModalActive));
  }

  // Get all visible, focusable eCRF form inputs
  function getVisibleEcrfInputs() {
    if (typeof document === "undefined") return [];
    const root = (formContainerRef && formContainerRef.value) || document;
    const selectors = [
      'input:not([type="hidden"]):not([type="submit"]):not([type="button"]):not([disabled])',
      'select:not([disabled])',
      'textarea:not([disabled])',
      '[tabindex="0"]:not([disabled])',
    ].join(", ");

    const isJsDom =
      typeof navigator !== "undefined" &&
      (navigator.userAgent.includes("jsdom") ||
        navigator.userAgent.includes("Node.js"));

    const elements = Array.from(root.querySelectorAll(selectors));
    return elements.filter((el) => {
      // Must be visible in viewport
      const style = window.getComputedStyle(el);
      if (style.display === "none" || style.visibility === "hidden") return false;
      if (!isJsDom && el.offsetParent === null && style.position !== "fixed") return false;
      return true;
    });
  }

  // Extract field ID associated with current focused element
  function getFocusedFieldId(activeEl) {
    if (!activeEl || !activeEl.isConnected) return null;

    // Direct ID check
    if (
      activeEl.id &&
      !activeEl.id.startsWith("query-") &&
      !activeEl.id.startsWith("sdv-") &&
      !activeEl.id.startsWith("batch-sdv-")
    ) {
      return activeEl.id;
    }

    // Check parent containers for field ID
    const container = activeEl.closest(
      '[id^="field-container-"], [id^="edit-check-banner-"], [data-field-id]'
    );
    if (container) {
      if (container.dataset && container.dataset.fieldId) {
        return container.dataset.fieldId;
      }
      const cId = container.getAttribute("id");
      if (cId && cId.startsWith("field-container-")) {
        return cId.replace("field-container-", "");
      }
      if (cId && cId.startsWith("edit-check-banner-")) {
        return cId.replace("edit-check-banner-", "");
      }
    }

    const wrapper = activeEl.closest(
      ".clinical-form-field-wrapper, .clinical-input, .form-group"
    );
    if (wrapper) {
      const input = wrapper.querySelector("input[id], select[id], textarea[id]");
      if (input && input.id) return input.id;
    }

    return activeEl.id || null;
  }

  // Handle Enter key auto-advance
  function handleEnterKey(event) {
    const activeEl = document.activeElement;
    if (!activeEl || !activeEl.isConnected) return false;

    const tagName = activeEl.tagName.toLowerCase();
    // Allow standard Enter behavior in textareas and buttons
    if (tagName === "textarea" || tagName === "button") return false;

    const inputs = getVisibleEcrfInputs();
    const currentIndex = inputs.indexOf(activeEl);

    if (currentIndex !== -1) {
      event.preventDefault();
      const nextIndex = currentIndex + 1;
      if (nextIndex < inputs.length) {
        const nextInput = inputs[nextIndex];
        nextInput.focus();
        if (
          nextInput.tagName.toLowerCase() === "input" &&
          ["text", "number", "email", "search"].includes(nextInput.type) &&
          typeof nextInput.select === "function"
        ) {
          nextInput.select();
        }
      }
      return true;
    }
    return false;
  }

  // Handle Alt+Q: Toggle query creation / response drawer
  function handleToggleQuery(event) {
    event.preventDefault();
    const activeEl = document.activeElement;
    const fieldId = getFocusedFieldId(activeEl);

    if (fieldId) {
      const queryFlag =
        document.getElementById(`query-flag-${fieldId}`) ||
        document.querySelector(`[aria-controls="query-panel-${fieldId}"]`) ||
        activeEl
          .closest(".clinical-form-field-wrapper, .clinical-input, .form-group")
          ?.querySelector(".query-flag");

      if (queryFlag) {
        queryFlag.click();
        return;
      }
    }

    // Fallback: click first query flag
    const queryFlags = Array.from(document.querySelectorAll(".query-flag"));
    if (queryFlags.length > 0) {
      queryFlags[0].click();
    }
  }

  // Handle Alt+N / Alt+P: Navigate between fields with active queries
  function handleQueryNavigation(event, direction) {
    event.preventDefault();
    const allQueryFlags = Array.from(document.querySelectorAll(".query-flag"));

    // Active query flags have non-none status classes or warning icon
    const activeQueryFlags = allQueryFlags.filter((flag) => {
      const classes = Array.from(flag.classList);
      const isNotNone = classes.some(
        (c) => c.startsWith("query-status-") && !c.includes("none")
      );
      const hasWarning = (flag.textContent || "").includes("⚠️");
      return isNotNone || hasWarning;
    });

    const targetFlags =
      activeQueryFlags.length > 0 ? activeQueryFlags : allQueryFlags;
    if (targetFlags.length === 0) return;

    const activeEl = document.activeElement;
    const currentFieldId = getFocusedFieldId(activeEl);

    let currentIndex = -1;
    if (currentFieldId) {
      currentIndex = targetFlags.findIndex((flag) => {
        const flagId = flag.id ? flag.id.replace("query-flag-", "") : "";
        return flagId === currentFieldId;
      });
    }

    let nextIndex;
    if (direction === "next") {
      nextIndex =
        currentIndex >= 0 ? (currentIndex + 1) % targetFlags.length : 0;
    } else {
      nextIndex =
        currentIndex >= 0
          ? (currentIndex - 1 + targetFlags.length) % targetFlags.length
          : targetFlags.length - 1;
    }

    const targetFlag = targetFlags[nextIndex];
    if (targetFlag) {
      const fieldId = targetFlag.id
        ? targetFlag.id.replace("query-flag-", "")
        : null;
      if (fieldId) {
        const input = document.getElementById(fieldId);
        if (input) {
          input.focus();
          return;
        }
      }
      targetFlag.focus();
    }
  }

  // Handle Alt+V: CRA Monitor toggle SDV status for active field
  function handleToggleSdv(event) {
    event.preventDefault();
    if (!getIsCra()) {
      console.warn("SDV shortcut (Alt+V) is restricted to CRA Monitor role.");
      return;
    }

    const activeEl = document.activeElement;
    const fieldId = getFocusedFieldId(activeEl);

    if (fieldId) {
      const sdvCheckbox = document.getElementById(`sdv-${fieldId}`);
      if (sdvCheckbox) {
        sdvCheckbox.click();
        return;
      }
    }

    // Fallback: Click first visible SDV checkbox in form
    const firstSdv = document.querySelector('.sdv-box input[type="checkbox"]');
    if (firstSdv) {
      firstSdv.click();
    }
  }

  // Keydown event dispatcher
  function handleKeyDown(event) {
    // Prevent duplicate processing if another composable instance handled this event
    if (event.defaultPrevented || event._ecrfShortcutHandled) {
      return;
    }

    // If a modal or dialog is active (other than our hotkey overlay), pause form shortcuts
    if (getIsModalOpen()) {
      if (showHotkeyOverlay.value && event.key === "Escape") {
        event.preventDefault();
        event._ecrfShortcutHandled = true;
        showHotkeyOverlay.value = false;
      }
      return;
    }

    const activeEl = document.activeElement;
    const isConnected =
      activeEl && activeEl.isConnected && activeEl !== document.body;
    const tagName = isConnected ? activeEl.tagName.toLowerCase() : "";
    const isEditableField =
      isConnected &&
      (tagName === "textarea" ||
        (tagName === "input" &&
          ["text", "number", "email", "password", "search"].includes(
            activeEl.type
          )));

    // 1. Hotkey Overlay: '?' (when not typing) or Ctrl+/ / Cmd+/
    if (
      (event.key === "?" && !isEditableField) ||
      ((event.ctrlKey || event.metaKey) && event.key === "/")
    ) {
      event.preventDefault();
      event._ecrfShortcutHandled = true;
      toggleHotkeyOverlay();
      return;
    }

    // Escape closes hotkey overlay if open
    if (showHotkeyOverlay.value && event.key === "Escape") {
      event.preventDefault();
      event._ecrfShortcutHandled = true;
      showHotkeyOverlay.value = false;
      return;
    }

    // 2. Submit eCRF: Ctrl+S, Cmd+S, or Alt+S
    if (
      ((event.ctrlKey || event.metaKey) &&
        (event.key === "s" || event.key === "S")) ||
      (event.altKey && (event.key === "s" || event.key === "S"))
    ) {
      event.preventDefault();
      event._ecrfShortcutHandled = true;
      if (typeof onSubmit === "function") {
        onSubmit();
      }
      return;
    }

    // 3. Enter key in eCRF field -> Auto advance focus
    if (
      event.key === "Enter" &&
      !event.shiftKey &&
      !event.ctrlKey &&
      !event.altKey &&
      !event.metaKey
    ) {
      if (handleEnterKey(event)) {
        event._ecrfShortcutHandled = true;
      }
      return;
    }

    // 4. Alt+Q: Toggle query drawer/panel
    if (event.altKey && (event.key === "q" || event.key === "Q")) {
      event.preventDefault();
      event._ecrfShortcutHandled = true;
      handleToggleQuery(event);
      return;
    }

    // 5. Alt+N / Alt+P: Navigate active query fields
    if (event.altKey && (event.key === "n" || event.key === "N")) {
      event.preventDefault();
      event._ecrfShortcutHandled = true;
      handleQueryNavigation(event, "next");
      return;
    }
    if (event.altKey && (event.key === "p" || event.key === "P")) {
      event.preventDefault();
      event._ecrfShortcutHandled = true;
      handleQueryNavigation(event, "prev");
      return;
    }

    // 6. Alt+V: Toggle SDV verification status (CRA Monitors only)
    if (event.altKey && (event.key === "v" || event.key === "V")) {
      event.preventDefault();
      event._ecrfShortcutHandled = true;
      handleToggleSdv(event);
      return;
    }
  }

  onMounted(() => {
    if (typeof window !== "undefined") {
      window.addEventListener("keydown", handleKeyDown);
    }
  });

  onUnmounted(() => {
    if (typeof window !== "undefined") {
      window.removeEventListener("keydown", handleKeyDown);
    }
  });

  return {
    showHotkeyOverlay,
    toggleHotkeyOverlay,
    openHotkeyOverlay,
    closeHotkeyOverlay,
    handleKeyDown,
  };
}

