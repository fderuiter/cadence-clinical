import DefaultTheme from 'vitepress/theme'
import { onMounted, watch } from 'vue'
import { useRoute } from 'vitepress'
import './custom.css'

export default {
  extends: DefaultTheme,
  setup() {
    const route = useRoute()

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
      let startX = 0;
      let startY = 0;

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

      // Drag and drop / panning functionality on the wrapper
      wrapper.addEventListener('mousedown', (e) => {
        if (e.button !== 0) return; // Only left click
        isDragging = true;
        startX = e.clientX - panX;
        startY = e.clientY - panY;
        wrapper.classList.add('active');
        e.preventDefault();
      });

      window.addEventListener('mousemove', (e) => {
        if (!isDragging) return;
        panX = e.clientX - startX;
        panY = e.clientY - startY;
        updateTransform();
      });

      window.addEventListener('mouseup', () => {
        isDragging = false;
        wrapper.classList.remove('active');
      });

      // Touch panning
      wrapper.addEventListener('touchstart', (e) => {
        if (e.touches.length === 1) {
          isDragging = true;
          startX = e.touches[0].clientX - panX;
          startY = e.touches[0].clientY - panY;
        }
      });

      wrapper.addEventListener('touchmove', (e) => {
        if (!isDragging || e.touches.length !== 1) return;
        panX = e.touches[0].clientX - startX;
        panY = e.touches[0].clientY - startY;
        updateTransform();
      });

      wrapper.addEventListener('touchend', () => {
        isDragging = false;
      });
    };

    onMounted(() => {
      // Delay initialization slightly to let mermaid finish rendering
      setTimeout(initPanZoom, 200);
    })

    watch(() => route.path, () => {
      setTimeout(initPanZoom, 600);
    })
  }
}
