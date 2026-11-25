import { defineAsyncComponent, defineComponent, h, shallowRef } from "vue";

function normalizeResolvedModule(mod) {
  return mod && typeof mod === "object" && "default" in mod ? mod.default : mod;
}

/**
 * Wraps a dynamically imported overlay or route component so production and demo
 * builds emit a pure `defineAsyncComponent` split chunk while Vitest unit tests
 * resolve cached components within a single `nextTick()` once their outer `v-if`
 * activation gate flips to `true`.
 *
 * @template T
 * @param {() => Promise<T>} loader
 */
export function defineLazyOverlay(loader) {
  if (import.meta.env?.MODE === "test") {
    const resolvedComponent = shallowRef(null);
    const preloadPromise = Promise.resolve()
      .then(() => loader())
      .then((mod) => {
        const comp = normalizeResolvedModule(mod);
        resolvedComponent.value = comp;
        return comp;
      });
    const pending = (globalThis.__CADENCE_LAZY_OVERLAY_PROMISES__ ??=
      new Set());
    if (typeof pending.add === "function") {
      pending.add(preloadPromise);
      preloadPromise.finally(() => {
        pending.delete(preloadPromise);
      });
    } else if (Array.isArray(pending)) {
      pending.push(preloadPromise);
    }
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
