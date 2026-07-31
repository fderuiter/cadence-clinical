import { onMounted, onUnmounted, nextTick } from "vue";

const FOCUSABLE_SELECTOR =
  'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

const isVisible = (el) => {
  if (typeof window === "undefined") return true;
  const style = window.getComputedStyle(el);
  if (style.display === "none" || style.visibility === "hidden") {
    return false;
  }
  // In JSDOM/Node.js environment, layout dimensions like offsetWidth/offsetHeight are always 0.
  // So we rely on computed styles in JSDOM, but check actual dimensions in browser.
  const isJsdDom =
    navigator.userAgent.includes("jsdom") ||
    navigator.userAgent.includes("Node.js") ||
    typeof window.happyDOM !== "undefined";
  if (isJsdDom) {
    return true;
  }
  return el.offsetWidth > 0 || el.offsetHeight > 0;
};

export function useFocusTrap(targetRef) {
  let previousActiveElement = null;

  const handleKeyDown = (e) => {
    if (e.key !== "Tab") return;
    if (!targetRef.value) return;

    const container = targetRef.value;
    const focusableElements = Array.from(
      container.querySelectorAll(FOCUSABLE_SELECTOR)
    ).filter(isVisible);

    if (focusableElements.length === 0) {
      e.preventDefault();
      return;
    }

    const firstEl = focusableElements[0];
    const lastEl = focusableElements[focusableElements.length - 1];

    if (e.shiftKey) {
      if (document.activeElement === firstEl || !container.contains(document.activeElement)) {
        e.preventDefault();
        lastEl.focus();
      }
    } else {
      if (document.activeElement === lastEl || !container.contains(document.activeElement)) {
        e.preventDefault();
        firstEl.focus();
      }
    }
  };

  onMounted(async () => {
    previousActiveElement = document.activeElement;

    await nextTick();

    if (targetRef.value) {
      document.addEventListener("keydown", handleKeyDown);

      const focusableElements = Array.from(
        targetRef.value.querySelectorAll(FOCUSABLE_SELECTOR)
      ).filter(isVisible);

      if (focusableElements.length > 0) {
        focusableElements[0].focus();
      }
    }
  });

  onUnmounted(() => {
    document.removeEventListener("keydown", handleKeyDown);
    if (
      previousActiveElement &&
      document.body.contains(previousActiveElement) &&
      typeof previousActiveElement.focus === "function"
    ) {
      previousActiveElement.focus();
    }
  });
}
