import { defineAsyncComponent, defineComponent, h, shallowRef } from "vue";

/**
 * Wraps a dynamically imported overlay component so production and demo builds
 * emit a pure `defineAsyncComponent` split chunk while Vitest unit tests can
 * resolve cached overlay components within a single `nextTick()` once their
 * outer `v-if` activation gate flips to `true`.
 *
 * @template T
 * @param {() => Promise<T>} loader
 */
export function defineLazyOverlay(loader) {
  if (import.meta.env?.MODE === "test") {
    const resolvedComponent = shallowRef(null);
    const preloadPromise = loader().then((mod) => {
      const comp =
        mod && typeof mod === "object" && "default" in mod ? mod.default : mod;
      resolvedComponent.value = comp;
      return comp;
    });
    const pending = (globalThis.__CADENCE_LAZY_OVERLAY_PROMISES__ ??=
      new Set());
    pending.add(preloadPromise);
    preloadPromise.finally(() => {
      pending.delete(preloadPromise);
    });
    return defineComponent({
      name: "LazyOverlayHost",
      inheritAttrs: false,
      setup(_, { attrs, slots }) {
        return () =>
          resolvedComponent.value
            ? h(resolvedComponent.value, attrs, slots)
            : null;
      },
    });
  }
  return defineAsyncComponent(loader);
}

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

function isHiddenOrInertOverlay(el) {
  return Boolean(
    el &&
    (el.hasAttribute?.("hidden") ||
      el.hasAttribute?.("inert") ||
      el.getAttribute?.("aria-hidden") === "true")
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
    !isHiddenOrInertOverlay(activeDialog) &&
    (!excludeId ||
      (activeDialog.id !== excludeId && !activeDialog.closest?.(selector)))
  ) {
    return true;
  }
  const modalDialogs = document.querySelectorAll(
    '[aria-modal="true"], [role="alertdialog"]'
  );
  for (const dialog of modalDialogs) {
    if (isHiddenOrInertOverlay(dialog)) continue;
    if (excludeId && (dialog.id === excludeId || dialog.closest?.(selector))) {
      continue;
    }
    return true;
  }
  return false;
}
