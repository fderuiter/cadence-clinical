function readCssProperty(tokenName: string, target?: Element | null): string {
  if (typeof window === "undefined" || typeof document === "undefined") {
    return "";
  }
  const root = document.documentElement;
  const primaryEl = target && target.isConnected ? target : root;
  const primaryVal = window
    .getComputedStyle(primaryEl)
    .getPropertyValue(tokenName)
    .trim();
  if (primaryVal) {
    return primaryVal;
  }
  if (primaryEl !== root) {
    return window.getComputedStyle(root).getPropertyValue(tokenName).trim();
  }
  return "";
}

function unwrapCssVarValue(
  rawValue: string,
  target?: Element | null,
  depth = 0
): string {
  if (!rawValue || depth > 8) return "";
  const varMatch = rawValue.match(
    /^var\(\s*(--[a-zA-Z0-9_-]+)(?:\s*,\s*([^)]+))?\)$/
  );
  if (!varMatch) return rawValue;
  const resolved = unwrapCssVarValue(
    readCssProperty(varMatch[1], target),
    target,
    depth + 1
  );
  if (resolved) return resolved;
  const fallback = varMatch[2]?.trim() ?? "";
  return fallback ? unwrapCssVarValue(fallback, target, depth + 1) : "";
}

/**
 * Resolves a design-system CSS custom property from the active DOM theme into a
 * concrete CSS color string suitable for HTML5 Canvas 2D drawing contexts
 * (guaranteeing drawing surfaces never receive unresolved `var(--...)` names).
 */
export function resolveThemeToken(
  tokenName: string,
  target?: Element | null
): string {
  return unwrapCssVarValue(readCssProperty(tokenName, target), target);
}

/**
 * Subscribes to `data-theme` / `.dark` attribute mutations and system
 * `prefers-color-scheme: dark` transitions, invoking `onThemeChange` so
 * imperative rendering surfaces (such as `<canvas>`) repaint immediately.
 */
export function observeActiveThemeChanges(
  onThemeChange: () => void
): () => void {
  const cleanups: Array<() => void> = [];

  if (
    typeof MutationObserver !== "undefined" &&
    typeof document !== "undefined"
  ) {
    const observer = new MutationObserver(() => {
      onThemeChange();
    });
    observer.observe(document.documentElement, {
      attributes: true,
      attributeFilter: ["data-theme", "class"],
    });
    cleanups.push(() => observer.disconnect());
  }

  if (
    typeof window !== "undefined" &&
    typeof window.matchMedia === "function"
  ) {
    const mql = window.matchMedia("(prefers-color-scheme: dark)");
    const listener = () => onThemeChange();
    if (typeof mql.addEventListener === "function") {
      mql.addEventListener("change", listener);
      cleanups.push(() => mql.removeEventListener("change", listener));
    }
  }

  return () => {
    for (const fn of cleanups) fn();
  };
}
