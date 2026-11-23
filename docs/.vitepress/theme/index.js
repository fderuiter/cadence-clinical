import DefaultTheme from "vitepress/theme";
import { nextTick, onMounted, onUnmounted, watch } from "vue";
import { useRoute } from "vitepress";
import BlogLayout from "./BlogLayout.vue";
import BlogIndex from "./components/BlogIndex.vue";
import "./custom.css";

let mermaidPromise = null;
let mermaidRenderSeq = 0;

function loadMermaidRuntime() {
  if (!mermaidPromise) {
    mermaidPromise = import("mermaid").then((mod) => mod.default ?? mod);
  }
  return mermaidPromise;
}

function getCurrentMermaidTheme() {
  if (typeof document === "undefined") return "default";
  return document.documentElement.classList.contains("dark")
    ? "dark"
    : "default";
}

function escapeSequenceDiagramSemicolons(source) {
  if (!/^\s*sequenceDiagram\b/m.test(source)) {
    return null;
  }
  const escaped = source.replace(
    /^(\s*[^:\r\n]+-[>-]+>?[+-]?\s*[^:\r\n]+:)(.+)$/gm,
    (_line, prefix, message) => {
      const safeMessage = message.replace(/(?<!#[a-zA-Z0-9]+);/g, "#59;");
      return `${prefix}${safeMessage}`;
    }
  );
  return escaped === source ? null : escaped;
}

function removeMermaidTempNode(renderId) {
  if (typeof document === "undefined") return;
  for (const id of [renderId, `d${renderId}`]) {
    const node = document.getElementById(id);
    if (node && node.parentNode) {
      node.parentNode.removeChild(node);
    }
  }
}

async function renderDiagramSvg(mermaid, source) {
  const renderId = `cadence-mermaid-${++mermaidRenderSeq}`;
  try {
    const { svg } = await mermaid.render(renderId, source);
    return svg;
  } catch (firstErr) {
    removeMermaidTempNode(renderId);
    const fallbackSource = escapeSequenceDiagramSemicolons(source);
    if (!fallbackSource) {
      console.error("Failed to render Mermaid diagram:", firstErr);
      return null;
    }
    const retryId = `cadence-mermaid-${++mermaidRenderSeq}`;
    try {
      const { svg } = await mermaid.render(retryId, fallbackSource);
      return svg;
    } catch (retryErr) {
      removeMermaidTempNode(retryId);
      console.error("Failed to render Mermaid diagram:", retryErr);
      return null;
    }
  }
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
    let themeObserver = null;
    let activeRenderToken = 0;

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

    onUnmounted(() => {
      clearAllActiveDrags();
      if (themeObserver) {
        themeObserver.disconnect();
        themeObserver = null;
      }
    });

    const enhanceDiagramContainer = (container) => {
      if (container.getAttribute("data-enhanced") === "true") {
        return;
      }
      const parent = container.parentNode;
      if (!parent) return;

      container.setAttribute("data-enhanced", "true");

      const outerContainer = document.createElement("div");
      outerContainer.className = "mermaid-container";

      const wrapper = document.createElement("div");
      wrapper.className = "mermaid-wrapper";

      parent.replaceChild(outerContainer, container);
      outerContainer.appendChild(wrapper);
      wrapper.appendChild(container);

      let scale = 1;
      let panX = 0;
      let panY = 0;
      let isDragging = false;

      const getSvg = () => container.querySelector("svg");

      const updateTransform = () => {
        const svg = getSvg();
        if (svg) {
          svg.style.transform = `translate(${panX}px, ${panY}px) scale(${scale})`;
        }
      };

      container.__applyMermaidTransform = updateTransform;

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

      let D_start = 0;
      let S_start = 1;
      let panX_start = 0;
      let panY_start = 0;
      let C_start = { x: 0, y: 0 };

      const initMultiTouch = () => {
        if (activePointers.size !== 2) return;
        const [p1, p2] = Array.from(activePointers.values());
        D_start = Math.hypot(p1.clientX - p2.clientX, p1.clientY - p2.clientY);
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
            updateTransform();
          }
        }
      };

      const releasePointer = (e) => {
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
        const svg = getSvg();
        if (svg) {
          svg.style.transition = "";
        }
        if (listenersAttached) {
          window.removeEventListener("pointermove", handlePointerMove);
          window.removeEventListener("pointerup", releasePointer);
          window.removeEventListener("pointercancel", releasePointer);
          listenersAttached = false;
        }
        activeCleanups.delete(cleanupDrag);
      };

      wrapper.style.touchAction = "none";

      wrapper.addEventListener("pointerdown", (e) => {
        if (e.pointerType === "mouse" && e.button !== 0) return;

        isDragging = true;
        activePointers.set(e.pointerId, e);
        const svg = getSvg();
        if (svg) {
          svg.style.transition = "none";
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
          window.addEventListener("pointerup", releasePointer);
          window.addEventListener("pointercancel", releasePointer);
          listenersAttached = true;
          activeCleanups.add(cleanupDrag);
        }
      });

      updateTransform();
    };

    const enhanceExistingSvgContainers = () => {
      if (typeof window === "undefined") return;
      for (const el of document.querySelectorAll(".mermaid")) {
        if (el.isConnected && el.querySelector("svg")) {
          enhanceDiagramContainer(el);
          if (typeof el.__applyMermaidTransform === "function") {
            el.__applyMermaidTransform();
          }
        }
      }
    };

    const renderPageDiagrams = async ({ forceThemeRefresh = false } = {}) => {
      if (typeof window === "undefined") return;

      const elements = Array.from(document.querySelectorAll(".mermaid"));
      if (elements.length === 0) {
        return;
      }

      const themeName = getCurrentMermaidTheme();
      const pendingElements = [];

      for (const el of elements) {
        if (!el.isConnected) continue;
        const encoded = el.getAttribute("data-mermaid-code");
        const hasSvg = Boolean(el.querySelector("svg"));
        const source = encoded
          ? decodeURIComponent(encoded)
          : hasSvg
            ? ""
            : (el.textContent ?? "").trim();

        if (hasSvg && (!forceThemeRefresh || !source)) {
          enhanceDiagramContainer(el);
          if (typeof el.__applyMermaidTransform === "function") {
            el.__applyMermaidTransform();
          }
          continue;
        }

        if (!source) continue;
        if (!encoded) {
          el.setAttribute("data-mermaid-code", encodeURIComponent(source));
        }

        const alreadyRenderedForTheme =
          !forceThemeRefresh &&
          el.getAttribute("data-rendered-theme") === themeName &&
          hasSvg;
        if (alreadyRenderedForTheme) {
          enhanceDiagramContainer(el);
          if (typeof el.__applyMermaidTransform === "function") {
            el.__applyMermaidTransform();
          }
          continue;
        }

        pendingElements.push({ el, source });
      }

      if (pendingElements.length === 0) {
        return;
      }

      const token = ++activeRenderToken;
      const mermaid = await loadMermaidRuntime();
      if (token !== activeRenderToken) return;

      mermaid.initialize({
        startOnLoad: false,
        securityLevel: "strict",
        theme: themeName,
      });

      for (const { el, source } of pendingElements) {
        if (token !== activeRenderToken || !el.isConnected) return;

        const renderedSvg = await renderDiagramSvg(mermaid, source);
        if (!renderedSvg) {
          continue;
        }
        if (token !== activeRenderToken || !el.isConnected) return;
        el.innerHTML = renderedSvg;
        el.setAttribute("data-rendered-theme", themeName);

        enhanceDiagramContainer(el);
        if (typeof el.__applyMermaidTransform === "function") {
          el.__applyMermaidTransform();
        }
      }

      if (!themeObserver && typeof MutationObserver !== "undefined") {
        let lastObservedDark =
          document.documentElement.classList.contains("dark");
        themeObserver = new MutationObserver(() => {
          const nowDark = document.documentElement.classList.contains("dark");
          if (nowDark !== lastObservedDark) {
            lastObservedDark = nowDark;
            void renderPageDiagrams({ forceThemeRefresh: true });
          }
        });
        themeObserver.observe(document.documentElement, {
          attributes: true,
          attributeFilter: ["class"],
        });
      }
    };

    const scheduleDiagramCheck = () => {
      enhanceExistingSvgContainers();
      void nextTick(() => {
        void renderPageDiagrams();
      });
    };

    onMounted(() => {
      scheduleDiagramCheck();
    });

    watch(
      () => route.path,
      () => {
        clearAllActiveDrags();
        scheduleDiagramCheck();
      }
    );
  },
};
