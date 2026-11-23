<template>
  <div
    ref="barRef"
    class="clinical-form-action-bar"
    :class="{
      'clinical-form-action-bar--tray-open': trayOpen,
      'clinical-form-action-bar--compact': compact,
      'clinical-form-action-bar--below-fold': isBelowFold,
    }"
    role="group"
    :aria-label="ariaLabel"
    data-testid="clinical-form-action-bar"
  >
    <div
      v-if="$slots.tray"
      v-show="trayOpen"
      class="clinical-form-action-bar__tray"
      role="group"
      :aria-label="trayLabel"
      data-testid="clinical-form-action-bar-tray"
    >
      <slot name="tray" />
    </div>

    <div class="clinical-form-action-bar__row">
      <div
        v-if="$slots.status || statusHint || $slots.secondary"
        class="clinical-form-action-bar__leading"
      >
        <div
          v-if="$slots.status || statusHint"
          :id="statusHintId || undefined"
          :data-testid="statusHintTestId || undefined"
          class="clinical-form-action-bar__status"
          :class="`clinical-form-action-bar__status--${statusTone}`"
        >
          <slot name="status">
            <Lock
              v-if="statusTone === 'gated'"
              :size="14"
              aria-hidden="true"
              class="clinical-form-action-bar__status-icon"
            />
            <Info
              v-else
              :size="14"
              aria-hidden="true"
              class="clinical-form-action-bar__status-icon"
            />
            <span class="clinical-form-action-bar__status-text">{{
              statusHint
            }}</span>
          </slot>
        </div>

        <div
          v-if="$slots.secondary"
          class="clinical-form-action-bar__secondary"
        >
          <slot name="secondary" />
        </div>
      </div>

      <div class="clinical-form-action-bar__actions">
        <slot />
      </div>
    </div>
  </div>
</template>

<script setup>
import { onBeforeUnmount, onMounted, ref } from "vue";
import { Info, Lock } from "lucide-vue-next";

defineProps({
  ariaLabel: {
    type: String,
    default: "Form actions",
  },
  trayOpen: {
    type: Boolean,
    default: false,
  },
  trayLabel: {
    type: String,
    default: "Action details",
  },
  statusHint: {
    type: String,
    default: "",
  },
  statusTone: {
    type: String,
    default: "muted",
  },
  statusHintId: {
    type: String,
    default: "",
  },
  statusHintTestId: {
    type: String,
    default: "",
  },
  compact: {
    type: Boolean,
    default: true,
  },
});

const barRef = ref(null);
const isBelowFold = ref(false);
let resizeObserver = null;

function updateFoldState() {
  const el = barRef.value;
  if (!el || typeof window === "undefined") return;
  const parent = el.parentElement;
  if (!parent) return;

  let bottomOffset = 0;
  const navTabs = document.querySelector(".nav-tabs");
  if (navTabs && window.getComputedStyle(navTabs).position === "fixed") {
    const navRect = navTabs.getBoundingClientRect();
    if (navRect.height > 0 && navRect.top < window.innerHeight) {
      bottomOffset = Math.max(0, window.innerHeight - navRect.top);
    }
  }

  const viewportBottom = window.innerHeight - bottomOffset;
  const parentRect = parent.getBoundingClientRect();
  const isMobileViewport = window.innerWidth < 768;
  const formAnchor =
    isMobileViewport && parent.tagName !== "FORM"
      ? parent.querySelector("form")
      : null;
  const anchorTop = formAnchor
    ? formAnchor.getBoundingClientRect().top
    : parentRect.top;
  const barHeight = el.offsetHeight || 52;
  const below =
    parentRect.bottom > viewportBottom &&
    viewportBottom - barHeight - anchorTop < 80;

  if (isBelowFold.value !== below) {
    isBelowFold.value = below;
  }
  el.classList.toggle("clinical-form-action-bar--below-fold", below);
}

onMounted(() => {
  updateFoldState();
  window.addEventListener("scroll", updateFoldState, {
    passive: true,
    capture: true,
  });
  window.addEventListener("resize", updateFoldState, { passive: true });
  if (typeof ResizeObserver !== "undefined") {
    resizeObserver = new ResizeObserver(updateFoldState);
    if (barRef.value) {
      resizeObserver.observe(barRef.value);
      if (barRef.value.parentElement) {
        resizeObserver.observe(barRef.value.parentElement);
      }
    }
  }
});

onBeforeUnmount(() => {
  if (typeof window !== "undefined") {
    window.removeEventListener("scroll", updateFoldState, { capture: true });
    window.removeEventListener("resize", updateFoldState);
  }
  if (resizeObserver) {
    resizeObserver.disconnect();
    resizeObserver = null;
  }
});
</script>

<style>
.clinical-form-action-bar {
  position: sticky;
  bottom: var(--form-action-bar-bottom, 0px);
  z-index: 20;
  width: 100%;
  box-sizing: border-box;
  margin-top: var(--spacing-md, 16px);
  background-color: var(--semantic-color-surface);
  border-top: 1px solid var(--semantic-color-border);
  font-family: var(--font-sans);
}

.clinical-form-action-bar--below-fold {
  position: static;
}

.clinical-form-action-bar__tray {
  padding: var(--spacing-xs, 8px) var(--spacing-sm, 12px);
  background-color: var(--semantic-color-surface-muted);
  border-bottom: 1px solid var(--semantic-color-border);
  box-sizing: border-box;
}

.clinical-form-action-bar__row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--spacing-xs, 8px);
  padding: 3px var(--spacing-sm, 12px);
  min-height: 50px;
  box-sizing: border-box;
}

.clinical-form-action-bar__leading {
  display: flex;
  align-items: center;
  gap: var(--spacing-xs, 8px);
  min-width: 0;
  flex: 1 1 auto;
}

.clinical-form-action-bar__status {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  min-width: 0;
  font-size: 0.8125rem;
  line-height: 1.2;
  color: var(--semantic-color-text-muted);
}

.clinical-form-action-bar__status--gated {
  color: var(--semantic-color-text-muted);
}

.clinical-form-action-bar__status-icon {
  flex: 0 0 auto;
}

.clinical-form-action-bar__status-text {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
}

.clinical-form-action-bar__secondary,
.clinical-form-action-bar__actions {
  display: flex;
  align-items: center;
  gap: var(--spacing-xs, 8px);
  flex: 0 0 auto;
}

.clinical-form-action-bar__actions {
  margin-left: auto;
  justify-content: flex-end;
}

.clinical-form-action-bar__secondary button,
.clinical-form-action-bar__secondary .btn,
.clinical-form-action-bar__actions button,
.clinical-form-action-bar__actions .btn {
  min-height: var(--form-action-bar-target-min, 44px);
  min-width: var(--form-action-bar-target-min, 44px);
  padding-block: 6px;
  padding-inline: 12px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  box-sizing: border-box;
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}

@media (max-width: 767px) {
  .clinical-form-action-bar__row {
    flex-wrap: wrap;
    padding: 6px var(--spacing-xs, 8px);
  }

  .clinical-form-action-bar__leading {
    flex-wrap: wrap;
    width: 100%;
  }

  .clinical-form-action-bar__status-text {
    display: inline;
    overflow: visible;
    text-overflow: clip;
  }

  .clinical-form-action-bar__secondary {
    flex-wrap: wrap;
    width: 100%;
  }

  .clinical-form-action-bar__actions {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(124px, 1fr));
    gap: 6px;
    width: 100%;
  }

  .clinical-form-action-bar__actions > button,
  .clinical-form-action-bar__actions > .btn,
  .clinical-form-action-bar__secondary > button,
  .clinical-form-action-bar__secondary > .btn {
    flex: 1 1 auto;
    width: 100%;
    padding-inline: 8px;
    white-space: normal;
  }
}
</style>
