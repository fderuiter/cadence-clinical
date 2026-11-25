export { defineLazyOverlay } from "ui/portal-shell";

/**
 * Returns true when the event target or active element is an editable control
 * where single-key shortcuts such as `?` or `F1` must not intercept input.
 *
 * @param {unknown} node
 */
export function isEditableOrFormControl(node) {
  const el =
    node instanceof HTMLElement
      ? node
      : typeof document !== "undefined" &&
          document.activeElement instanceof HTMLElement
        ? document.activeElement
        : null;
  if (!el) return false;
  if (el.isContentEditable) return true;
  const tag = el.tagName?.toUpperCase?.() || "";
  if (["INPUT", "TEXTAREA", "SELECT", "OPTION"].includes(tag)) return true;
  if (
    typeof el.closest === "function" &&
    el.closest(
      '[contenteditable="true"], [contenteditable=""], [contenteditable="plaintext-only"]'
    )
  ) {
    return true;
  }
  const role = (el.getAttribute?.("role") || "").toLowerCase();
  return ["textbox", "searchbox", "combobox", "listbox", "spinbutton"].includes(
    role
  );
}

/**
 * Returns true when another modal or dialog outside `excludeId` is currently open.
 *
 * @param {string} [excludeId]
 */
export function hasBlockingModalDialog(excludeId = "contextual-help-drawer") {
  if (typeof document === "undefined") return false;
  const selector = excludeId ? `#${excludeId}` : "";
  const activeDialog = document.activeElement?.closest?.(
    '[aria-modal="true"], [role="dialog"], [role="alertdialog"]'
  );
  if (
    activeDialog &&
    (!excludeId ||
      (activeDialog.id !== excludeId && !activeDialog.closest?.(selector)))
  ) {
    return true;
  }
  const modalDialogs = document.querySelectorAll(
    '[aria-modal="true"], [role="alertdialog"]'
  );
  for (const dialog of modalDialogs) {
    if (excludeId && (dialog.id === excludeId || dialog.closest?.(selector))) {
      continue;
    }
    return true;
  }
  return false;
}
