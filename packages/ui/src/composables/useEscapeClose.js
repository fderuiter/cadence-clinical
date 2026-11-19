import {
  getCurrentInstance,
  getCurrentScope,
  onMounted,
  onScopeDispose,
  onUnmounted,
  unref,
} from "vue";
import { hasActiveFocusTrapWithEscape } from "./useFocusTrap.js";

export function useEscapeClose(onCloseCallback, options = {}) {
  const handledEscapeEvents = new WeakSet();
  const hasExplicitIsOpen =
    options !== null &&
    typeof options === "object" &&
    Object.prototype.hasOwnProperty.call(options, "isOpen") &&
    options.isOpen !== undefined;

  const isEnabled = () => {
    if (!hasExplicitIsOpen) return true;
    const raw = options.isOpen;
    return Boolean(typeof raw === "function" ? raw() : unref(raw));
  };

  const handleKeyDown = (e) => {
    if (!e || e.key !== "Escape" || handledEscapeEvents.has(e)) return;
    if (e.defaultPrevented || hasActiveFocusTrapWithEscape()) return;
    if (!isEnabled()) return;

    handledEscapeEvents.add(e);
    if (typeof onCloseCallback === "function") {
      onCloseCallback(e);
    }
  };

  if (getCurrentInstance()) {
    onMounted(() => {
      document.addEventListener("keydown", handleKeyDown);
      window.addEventListener("keydown", handleKeyDown);
    });

    onUnmounted(() => {
      document.removeEventListener("keydown", handleKeyDown);
      window.removeEventListener("keydown", handleKeyDown);
    });
  } else if (
    getCurrentScope() &&
    typeof document !== "undefined" &&
    typeof window !== "undefined"
  ) {
    document.addEventListener("keydown", handleKeyDown);
    window.addEventListener("keydown", handleKeyDown);
    onScopeDispose(() => {
      document.removeEventListener("keydown", handleKeyDown);
      window.removeEventListener("keydown", handleKeyDown);
    });
  }
}
