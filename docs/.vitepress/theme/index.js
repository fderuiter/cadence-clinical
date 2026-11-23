import DefaultTheme from "vitepress/theme";
import mermaid from "mermaid";
import { onMounted, onUnmounted, watch } from "vue";
import { useRoute } from "vitepress";
import BlogLayout from "./BlogLayout.vue";
import BlogIndex from "./components/BlogIndex.vue";
import "./custom.css";

if (typeof window !== "undefined") {
  mermaid.initialize({ startOnLoad: false });
}

export default {
  extends: DefaultTheme,
  Layout: BlogLayout,
  enhanceApp({ app }) {
    app.component("BlogIndex", BlogIndex);
  },
  setup() {
    const route = useRoute();
    const activeCleanups = new Set();
    const activeObservers = new Set();
    let rootObserver = null;

    const clearAllActiveDrags = () => {
      activeCleanups.forEach((cleanup) => {
        try {
          cleanup();
        } catch (err) {
          console.error("Error during drag cleanup:", err);
        }
      });
      activeCleanups.clear();
    };

    const disconnectAllObservers = () => {
      activeObservers.forEach((entry) => {
        try {
          entry.observer.disconnect();
        } catch {
          // Ignore observer disconnect errors during teardown.
        }
      });
      activeObservers.clear();
      if (rootObserver) {
        rootObserver.disconnect();
        rootObserver = null;
      }
    };

    const pruneDisconnectedObservers = () => {
      activeObservers.forEach((entry) => {
        if (!entry.container.isConnected) {
          try {
            entry.observer.disconnect();
          } catch {
            // Ignore observer disconnect errors during teardown.
          }
          activeObservers.delete(entry);
        }
      });
    };

    onUnmounted(() => {
      clearAllActiveDrags();
      disconnectAllObservers();
    });

    const initPanZoom = () => {
      if (typeof window === "undefined") return;

      const elements = document.querySelectorAll(".mermaid");
      elements.forEach((el) => {
        if (el.getAttribute("data-enhanced") === "true") {
          return;
        }
        el.setAttribute("data-enhanced", "true");
        observeAndEnhanceContainer(el);
      });
    };

    const observeAndEnhanceContainer = (container) => {
      let isWrapped = false;
      let scale = 1;
      let panX = 0;
      let panY = 0;
      let isDragging = false;
      let hasInteracted = false;

      const getLiveSvg = () => container.querySelector("svg");

      const updateTransform = () => {
        const currentSvg = getLiveSvg();
        if (!currentSvg) return;
        currentSvg.style.transition = isDragging ? "none" : "";
        currentSvg.style.transform = `translate(${panX}px, ${panY}px) scale(${scale})`;
      };

      let observerEntry = null;

      const syncLiveSvgOnMutation = () => {
        if (!container.isConnected) {
          containerObserver.disconnect();
          if (observerEntry) {
            activeObservers.delete(observerEntry);
          }
          return;
        }
        const currentSvg = getLiveSvg();
        if (!currentSvg) return;

        if (!isWrapped) {
          wrapContainer();
        }
        if (hasInteracted || scale !== 1 || panX !== 0 || panY !== 0) {
          updateTransform();
        } else if (isDragging) {
          currentSvg.style.transition = "none";
        }
      };

      const containerObserver = new MutationObserver(() => {
        syncLiveSvgOnMutation();
      });
      observerEntry = { container, observer: containerObserver };
      containerObserver.observe(container, { childList: true, subtree: true });
      activeObservers.add(observerEntry);

      const wrapContainer = () => {
        if (isWrapped || container.closest(".mermaid-container")) {
          isWrapped = true;
          return;
        }
        const parent = container.parentNode;
        if (!parent) return;
        isWrapped = true;

        const outerContainer = document.createElement("div");
        outerContainer.className = "mermaid-container";

        const wrapper = document.createElement("div");
        wrapper.className = "mermaid-wrapper";

        // Move the container inside the wrapper, and wrap it
        parent.replaceChild(outerContainer, container);
        outerContainer.appendChild(wrapper);
        wrapper.appendChild(container);

        // Add toolbar
        const toolbar = document.createElement("div");
        toolbar.className = "mermaid-toolbar";

        const btnZoomIn = document.createElement("button");
        btnZoomIn.type = "button";
        btnZoomIn.className = "mermaid-btn";
        btnZoomIn.innerText = "+";
        btnZoomIn.title = "Zoom In";
        btnZoomIn.setAttribute("aria-label", "Zoom in diagram");
        btnZoomIn.onclick = (e) => {
          e.stopPropagation();
          hasInteracted = true;
          scale = Math.min(scale * 1.2, 5);
          updateTransform();
        };

        const btnZoomOut = document.createElement("button");
        btnZoomOut.type = "button";
        btnZoomOut.className = "mermaid-btn";
        btnZoomOut.innerText = "-";
        btnZoomOut.title = "Zoom Out";
        btnZoomOut.setAttribute("aria-label", "Zoom out diagram");
        btnZoomOut.onclick = (e) => {
          e.stopPropagation();
          hasInteracted = true;
          scale = Math.max(scale / 1.2, 0.2);
          updateTransform();
        };

        const btnReset = document.createElement("button");
        btnReset.type = "button";
        btnReset.className = "mermaid-btn";
        btnReset.innerText = "↺";
        btnReset.title = "Reset";
        btnReset.setAttribute("aria-label", "Reset diagram zoom");
        btnReset.onclick = (e) => {
          e.stopPropagation();
          hasInteracted = true;
          scale = 1;
          panX = 0;
          panY = 0;
          updateTransform();
        };

        toolbar.appendChild(btnZoomIn);
        toolbar.appendChild(btnZoomOut);
        toolbar.appendChild(btnReset);
        outerContainer.appendChild(toolbar);

        let listenersAttached = false;
        const activePointers = new Map();
        let lastSinglePointerPos = null;
        let isMultiTouch = false;

        // Multi-touch initial metrics
        let D_start = 0;
        let S_start = 1;
        let panX_start = 0;
        let panY_start = 0;
        let C_start = { x: 0, y: 0 };

        const initMultiTouch = () => {
          if (activePointers.size !== 2) return;
          const [p1, p2] = Array.from(activePointers.values());
          D_start = Math.hypot(
            p1.clientX - p2.clientX,
            p1.clientY - p2.clientY
          );
          S_start = scale;
          panX_start = panX;
          panY_start = panY;
          C_start = {
            x: (p1.clientX + p2.clientX) / 2,
            y: (p1.clientY + p2.clientY) / 2,
          };
          isMultiTouch = true;
        };

        const handlePointerMove = (e) => {
          if (!isDragging) return;
          if (activePointers.has(e.pointerId)) {
            activePointers.set(e.pointerId, e);
          } else {
            return;
          }

          if (activePointers.size === 1) {
            const p = activePointers.get(e.pointerId);
            if (lastSinglePointerPos) {
              const dx = p.clientX - lastSinglePointerPos.x;
              const dy = p.clientY - lastSinglePointerPos.y;
              panX += dx;
              panY += dy;
              hasInteracted = true;
              lastSinglePointerPos = { x: p.clientX, y: p.clientY };
              updateTransform();
            }
          } else if (activePointers.size === 2) {
            if (!isMultiTouch) {
              initMultiTouch();
            }
            const [p1, p2] = Array.from(activePointers.values());
            const D_new = Math.hypot(
              p1.clientX - p2.clientX,
              p1.clientY - p2.clientY
            );
            const C_new = {
              x: (p1.clientX + p2.clientX) / 2,
              y: (p1.clientY + p2.clientY) / 2,
            };

            if (D_start > 0) {
              const f = D_new / D_start;
              const S_new = Math.min(Math.max(S_start * f, 0.2), 5);
              panX = C_new.x - (S_new * (C_start.x - panX_start)) / S_start;
              panY = C_new.y - (S_new * (C_start.y - panY_start)) / S_start;
              scale = S_new;
              hasInteracted = true;
              updateTransform();
            }
          }
        };

        const handlePointerUp = (e) => {
          activePointers.delete(e.pointerId);
          try {
            wrapper.releasePointerCapture(e.pointerId);
          } catch {
            // Pointer capture may already be released.
          }

          if (activePointers.size === 0) {
            cleanupDrag();
          } else if (activePointers.size === 1) {
            const [p] = Array.from(activePointers.values());
            lastSinglePointerPos = { x: p.clientX, y: p.clientY };
            isMultiTouch = false;
          } else if (activePointers.size === 2) {
            initMultiTouch();
          }
        };

        const handlePointerCancel = (e) => {
          activePointers.delete(e.pointerId);
          try {
            wrapper.releasePointerCapture(e.pointerId);
          } catch {
            // Pointer capture may already be released.
          }

          if (activePointers.size === 0) {
            cleanupDrag();
          } else if (activePointers.size === 1) {
            const [p] = Array.from(activePointers.values());
            lastSinglePointerPos = { x: p.clientX, y: p.clientY };
            isMultiTouch = false;
          } else if (activePointers.size === 2) {
            initMultiTouch();
          }
        };

        const cleanupDrag = () => {
          isDragging = false;
          isMultiTouch = false;
          activePointers.clear();
          lastSinglePointerPos = null;
          wrapper.classList.remove("active");
          const currentSvg = getLiveSvg();
          if (currentSvg) {
            currentSvg.style.transition = ""; // Restore transitions for toolbar buttons
          }
          if (listenersAttached) {
            window.removeEventListener("pointermove", handlePointerMove);
            window.removeEventListener("pointerup", handlePointerUp);
            window.removeEventListener("pointercancel", handlePointerCancel);
            listenersAttached = false;
          }
          activeCleanups.delete(cleanupDrag);
        };

        // Set touch-action programmatically to prevent scrolling and gesture actions
        wrapper.style.touchAction = "none";

        // Unified PointerEvents pipeline for interactions
        wrapper.addEventListener("pointerdown", (e) => {
          if (e.pointerType === "mouse" && e.button !== 0) return;

          isDragging = true;
          activePointers.set(e.pointerId, e);
          const currentSvg = getLiveSvg();
          if (currentSvg) {
            currentSvg.style.transition = "none"; // Temporarily disable transitions during drag
          }

          try {
            wrapper.setPointerCapture(e.pointerId);
          } catch {
            // Synthetic pointer events in headless environments may not support capture.
          }

          if (activePointers.size === 1) {
            lastSinglePointerPos = { x: e.clientX, y: e.clientY };
            wrapper.classList.add("active");
          } else if (activePointers.size === 2) {
            initMultiTouch();
          }

          if (!listenersAttached) {
            window.addEventListener("pointermove", handlePointerMove);
            window.addEventListener("pointerup", handlePointerUp);
            window.addEventListener("pointercancel", handlePointerCancel);
            listenersAttached = true;
            activeCleanups.add(cleanupDrag);
          }
        });
      };

      if (getLiveSvg()) {
        wrapContainer();
      }
    };

    onMounted(() => {
      initPanZoom();
      if (typeof document !== "undefined" && document.body) {
        rootObserver = new MutationObserver(() => {
          initPanZoom();
        });
        rootObserver.observe(document.body, { childList: true, subtree: true });
      }
    });

    watch(
      () => route.path,
      () => {
        clearAllActiveDrags();
        pruneDisconnectedObservers();
        setTimeout(() => {
          pruneDisconnectedObservers();
          initPanZoom();
        }, 50);
      }
    );
  },
};
