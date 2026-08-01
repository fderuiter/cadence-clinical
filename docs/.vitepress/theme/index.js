import DefaultTheme from 'vitepress/theme'
import { onMounted, onUnmounted, watch } from 'vue'
import { useRoute } from 'vitepress'
import './custom.css'

export default {
  extends: DefaultTheme,
  setup() {
    const route = useRoute()
    const activeCleanups = new Set()

    const clearAllActiveDrags = () => {
      activeCleanups.forEach((cleanup) => {
        try {
          cleanup();
        } catch (err) {
          console.error('Error during drag cleanup:', err);
        }
      });
      activeCleanups.clear();
    };

    onUnmounted(() => {
      clearAllActiveDrags();
    });

    const initPanZoom = () => {
      if (typeof window === 'undefined') return;

      const elements = document.querySelectorAll('.mermaid');
      elements.forEach((el) => {
        // Check if we already initialized this element
        if (el.getAttribute('data-enhanced') === 'true') {
          return;
        }
        el.setAttribute('data-enhanced', 'true');

        // Wait until the SVG is rendered inside the element
        const checkAndEnhance = () => {
          const svg = el.querySelector('svg');
          if (!svg) {
            // Retry in 100ms if SVG is not yet rendered by Mermaid
            setTimeout(checkAndEnhance, 100);
            return;
          }

          enhanceSvg(el, svg);
        };

        checkAndEnhance();
      });
    };

    const enhanceSvg = (container, svg) => {
      const parent = container.parentNode;
      if (!parent) return;

      const outerContainer = document.createElement('div');
      outerContainer.className = 'mermaid-container';

      const wrapper = document.createElement('div');
      wrapper.className = 'mermaid-wrapper';

      // Move the container inside the wrapper, and wrap it
      parent.replaceChild(outerContainer, container);
      outerContainer.appendChild(wrapper);
      wrapper.appendChild(container);

      // Set initial state
      let scale = 1;
      let panX = 0;
      let panY = 0;
      let isDragging = false;

      // Apply transforms to SVG
      const updateTransform = () => {
        svg.style.transform = `translate(${panX}px, ${panY}px) scale(${scale})`;
      };

      // Add toolbar
      const toolbar = document.createElement('div');
      toolbar.className = 'mermaid-toolbar';

      const btnZoomIn = document.createElement('button');
      btnZoomIn.className = 'mermaid-btn';
      btnZoomIn.innerText = '+';
      btnZoomIn.title = 'Zoom In';
      btnZoomIn.onclick = (e) => {
        e.stopPropagation();
        scale = Math.min(scale * 1.2, 5);
        updateTransform();
      };

      const btnZoomOut = document.createElement('button');
      btnZoomOut.className = 'mermaid-btn';
      btnZoomOut.innerText = '-';
      btnZoomOut.title = 'Zoom Out';
      btnZoomOut.onclick = (e) => {
        e.stopPropagation();
        scale = Math.max(scale / 1.2, 0.2);
        updateTransform();
      };

      const btnReset = document.createElement('button');
      btnReset.className = 'mermaid-btn';
      btnReset.innerText = '↺';
      btnReset.title = 'Reset';
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

      // Multi-touch initial metrics
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
          y: (p1.clientY + p2.clientY) / 2
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
          const D_new = Math.hypot(p1.clientX - p2.clientX, p1.clientY - p2.clientY);
          const C_new = {
            x: (p1.clientX + p2.clientX) / 2,
            y: (p1.clientY + p2.clientY) / 2
          };

          if (D_start > 0) {
            const f = D_new / D_start;
            const S_new = Math.min(Math.max(S_start * f, 0.2), 5);
            panX = C_new.x - S_new * (C_start.x - panX_start) / S_start;
            panY = C_new.y - S_new * (C_start.y - panY_start) / S_start;
            scale = S_new;
            updateTransform();
          }
        }
      };

      const handlePointerUp = (e) => {
        activePointers.delete(e.pointerId);
        try {
          wrapper.releasePointerCapture(e.pointerId);
        } catch (err) {}

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
        } catch (err) {}

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
        wrapper.classList.remove('active');
        svg.style.transition = ''; // Restore transitions for toolbar buttons
        if (listenersAttached) {
          window.removeEventListener('pointermove', handlePointerMove);
          window.removeEventListener('pointerup', handlePointerUp);
          window.removeEventListener('pointercancel', handlePointerCancel);
          listenersAttached = false;
        }
        activeCleanups.delete(cleanupDrag);
      };

      // Set touch-action programmatically to prevent scrolling and gesture actions
      wrapper.style.touchAction = 'none';

      // Unified PointerEvents pipeline for interactions
      wrapper.addEventListener('pointerdown', (e) => {
        if (e.pointerType === 'mouse' && e.button !== 0) return;

        isDragging = true;
        activePointers.set(e.pointerId, e);
        svg.style.transition = 'none'; // Temporarily disable transitions during drag

        try {
          wrapper.setPointerCapture(e.pointerId);
        } catch (err) {}

        if (activePointers.size === 1) {
          lastSinglePointerPos = { x: e.clientX, y: e.clientY };
          wrapper.classList.add('active');
        } else if (activePointers.size === 2) {
          initMultiTouch();
        }

        if (!listenersAttached) {
          window.addEventListener('pointermove', handlePointerMove);
          window.addEventListener('pointerup', handlePointerUp);
          window.addEventListener('pointercancel', handlePointerCancel);
          listenersAttached = true;
          activeCleanups.add(cleanupDrag);
        }
      });
    };

    onMounted(() => {
      // Delay initialization slightly to let mermaid finish rendering
      setTimeout(initPanZoom, 200);
    })

    watch(() => route.path, () => {
      clearAllActiveDrags();
      setTimeout(initPanZoom, 600);
    })
  }
}
