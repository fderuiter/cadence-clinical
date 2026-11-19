// Keeps Tab / Shift+Tab focus cycling inside a modal dialog: from the last
// enabled control back to the first, and the reverse.
const DEFAULT_FOCUSABLE_TAGS = [
  "input",
  "select",
  "textarea",
  "button",
  "a[href]",
  '[tabindex]:not([tabindex="-1"])',
];

export function trapTabKey(e, modalId, tags = DEFAULT_FOCUSABLE_TAGS) {
  if (e.key !== "Tab") return;
  const modal = document.getElementById(modalId);
  if (!modal) return;

  const mergedTags = Array.from(new Set([...tags, "a[href]"]));
  const selectors = mergedTags
    .map((tag) => `${tag}:not([disabled]):not([aria-hidden="true"])`)
    .join(", ");
  const candidates = Array.from(modal.querySelectorAll(selectors)).filter(
    (el) => !el.hidden && el.getAttribute("tabindex") !== "-1"
  );
  if (!candidates.length) return;

  const visible = candidates.filter((el) => el.offsetParent !== null);
  const elList = visible.length > 0 ? visible : candidates;

  const firstEl = elList[0];
  const lastEl = elList[elList.length - 1];
  const active = document.activeElement;

  if (e.shiftKey) {
    if (active === firstEl || !modal.contains(active) || active === modal) {
      lastEl.focus();
      e.preventDefault();
    }
  } else if (active === lastEl || !modal.contains(active) || active === modal) {
    firstEl.focus();
    e.preventDefault();
  }
}
