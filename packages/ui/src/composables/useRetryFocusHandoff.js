import { nextTick } from "vue";
import { resolveOption } from "./useFocusTrap.js";
import {
  findFocusableCandidate,
  handoffFocus,
  normalizeCandidateList,
  queryDocumentSelector,
  resolveFocusCandidateElement,
} from "./useInlineFocusHandoff.js";

const DEFAULT_RETRY_ERROR_SELECTORS = [
  '[data-testid="canvas-error-retry"]',
  ".btn-retry",
  'button[id*="retry"]',
];

const RECOVERED_INTERACTIVE_SELECTOR =
  'button:not([disabled]),a[href],input:not([disabled]):not([type="hidden"]),select:not([disabled]),textarea:not([disabled]),summary,[tabindex]:not([tabindex="-1"])';

const RECOVERED_HEADING_SELECTOR =
  'h1[tabindex="-1"],h2[tabindex="-1"],h3[tabindex="-1"],h4[tabindex="-1"],h1,h2,h3,h4';

const resolveEnclosingRetryContainer = (explicitContainer, triggerEl) => {
  const explicit = resolveOption(explicitContainer);
  if (explicit) return explicit.$el || explicit;
  return (
    triggerEl?.closest?.(
      'section, article, aside, [role="dialog"], [role="alertdialog"], main'
    ) ||
    triggerEl?.parentElement ||
    null
  );
};

const isInsideAlertRegion = (el, rootContainer) => {
  const alertEl = el?.closest?.('[role="alert"], .canvas-error-alert');
  return Boolean(
    alertEl &&
    alertEl !== rootContainer &&
    (!rootContainer || rootContainer.contains(alertEl))
  );
};

const collectRecoveredSuccessTargets = (container, anchorBeforeAlert) => {
  if (!container || typeof container.querySelectorAll !== "function") return [];
  const keep = (el) => !isInsideAlertRegion(el, container);
  const results = [];

  if (anchorBeforeAlert && anchorBeforeAlert.parentElement === container) {
    for (
      let cur = anchorBeforeAlert.nextElementSibling;
      cur;
      cur = cur.nextElementSibling
    ) {
      if (cur.matches?.(RECOVERED_INTERACTIVE_SELECTOR) && keep(cur)) {
        results.push(cur);
      }
      if (cur.querySelectorAll) {
        for (const item of cur.querySelectorAll(
          RECOVERED_INTERACTIVE_SELECTOR
        )) {
          if (keep(item)) results.push(item);
        }
      }
    }
  }

  for (const sel of [
    RECOVERED_HEADING_SELECTOR,
    RECOVERED_INTERACTIVE_SELECTOR,
  ]) {
    for (const item of container.querySelectorAll(sel)) {
      if (keep(item)) results.push(item);
    }
  }

  return results;
};

/**
 * Executes a retry callback (sync or async) and hands keyboard focus to the
 * retry button on repeated error or to recovered content / headings on success,
 * without trapping `Tab` or marking background regions `inert`.
 *
 * @param {() => any | Promise<any>} action
 * @param {Object} [options]
 * @returns {Promise<HTMLElement | null>}
 */
export async function handoffRetryFocus(action, options = {}) {
  const activeBefore =
    typeof document !== "undefined" ? document.activeElement : null;

  if (options.retryScope !== undefined && options.retryScope !== null) {
    const rawScope =
      resolveFocusCandidateElement(
        options.retryScope,
        resolveOption(options.container) || null
      ) ||
      (typeof options.retryScope === "string"
        ? queryDocumentSelector(options.retryScope, null)
        : resolveOption(options.retryScope));
    if (
      !rawScope ||
      !activeBefore ||
      (rawScope !== activeBefore && !rawScope.contains?.(activeBefore))
    ) {
      if (typeof action === "function") await action();
      return null;
    }
  }

  const alertEl =
    resolveOption(options.alertElement) ||
    activeBefore?.closest?.('[role="alert"], .canvas-error-alert') ||
    null;
  const capturedContainer = resolveEnclosingRetryContainer(
    options.container,
    alertEl || activeBefore
  );
  const anchorBeforeAlert = alertEl?.previousElementSibling || null;

  let actionThrew = false;
  let thrownError = null;
  try {
    if (typeof action === "function") await action();
  } catch (err) {
    actionThrew = true;
    thrownError = err;
  }

  await nextTick();

  const latestContainer =
    resolveEnclosingRetryContainer(options.container, null) ||
    capturedContainer;
  const errorTargets =
    options.errorTargets !== undefined
      ? options.errorTargets
      : DEFAULT_RETRY_ERROR_SELECTORS;
  const errorPredicate =
    typeof options.isError === "function"
      ? options.isError
      : typeof options.hasError === "function"
        ? options.hasError
        : null;
  const failed =
    actionThrew ||
    (errorPredicate
      ? Boolean(errorPredicate())
      : Boolean(
          findFocusableCandidate(
            normalizeCandidateList(errorTargets),
            latestContainer
          )
        ));

  const explicitSuccessTargets =
    options.successTargets !== undefined
      ? options.successTargets
      : options.recoveredTargets;

  const handoffOptions = {
    container: latestContainer,
    fallback: options.fallback,
    includeDefaultFallbacks: options.includeDefaultFallbacks,
    scrollIfOffscreen: options.scrollIfOffscreen,
    preventScroll: options.preventScroll,
    awaitNextTick: false,
  };

  const focused = await handoffFocus(
    failed
      ? errorTargets
      : explicitSuccessTargets !== undefined
        ? explicitSuccessTargets
        : () =>
            collectRecoveredSuccessTargets(latestContainer, anchorBeforeAlert),
    handoffOptions
  );

  if (actionThrew) throw thrownError;
  return focused;
}

/**
 * Composable wrapper around `handoffRetryFocus` with shared default options.
 *
 * @param {Object} [defaultOptions]
 */
export function useRetryFocusHandoff(defaultOptions = {}) {
  return {
    retryWithFocus: (action, overrideOptions = {}) =>
      handoffRetryFocus(action, { ...defaultOptions, ...overrideOptions }),
  };
}
