// Keeps Tab / Shift+Tab focus cycling inside a modal dialog: from the last
// enabled control back to the first, and the reverse.
export function trapTabKey(e, modalId, tags) {
  if (e.key !== "Tab") return;
  const modal = document.getElementById(modalId);
  if (!modal) return;

  const selectors = tags.map((tag) => `${tag}:not([disabled])`).join(", ");
  const elList = Array.from(modal.querySelectorAll(selectors));
  if (!elList.length) return;

  const firstEl = elList[0];
  const lastEl = elList[elList.length - 1];

  if (e.shiftKey) {
    if (document.activeElement === firstEl) {
      lastEl.focus();
      e.preventDefault();
    }
  } else {
    if (document.activeElement === lastEl) {
      firstEl.focus();
      e.preventDefault();
    }
  }
}
