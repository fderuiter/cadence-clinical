<script setup>
import { defineAsyncComponent } from "vue";

const EntityLink = defineAsyncComponent(() => import("./EntityLink.vue"));

defineProps({
  density: {
    type: String,
    default: "compact",
    validator: (val) => ["compact", "comfortable", "touch"].includes(val),
  },
  identifier: {
    type: String,
    default: "",
  },
  nodeRef: {
    type: [String, Object],
    default: null,
  },
  title: {
    type: String,
    default: "",
  },
  subtitle: {
    type: String,
    default: "",
  },
  status: {
    type: String,
    default: "",
  },
  fields: {
    type: Array,
    default: () => [],
  },
  masked: {
    type: Boolean,
    default: false,
  },
  maskedMessage: {
    type: String,
    default: "Restricted clinical record",
  },
  selected: {
    type: Boolean,
    default: false,
  },
  interactive: {
    type: Boolean,
    default: false,
  },
  cardId: {
    type: String,
    default: "",
  },
  cardClass: {
    type: [String, Array, Object],
    default: "",
  },
  cardAttrs: {
    type: Object,
    default: null,
  },
});

const emit = defineEmits(["select", "card-click"]);

const INTERACTIVE_CHILD_SELECTOR =
  'button, a, input, select, textarea, label, summary, [role="button"], [role="link"], [role="checkbox"], [role="switch"], [role="menuitem"], [tabindex]';

function isCardSelfTarget(event) {
  const { target, currentTarget } = event;
  if (!target || !currentTarget) return false;
  if (target === currentTarget) return true;
  if (typeof target.closest !== "function") return false;
  return target.closest(INTERACTIVE_CHILD_SELECTOR) === currentTarget;
}

function onCardClick(event) {
  if (!isCardSelfTarget(event)) return;
  emit("card-click", event);
}

function onCardKeydown(event) {
  if (event.target !== event.currentTarget) return;
  if (event.key === "Enter" || event.key === " ") {
    event.preventDefault();
    emit("card-click", event);
  }
}
</script>

<template>
  <article
    :id="cardId || undefined"
    :class="[
      'clinical-record-card',
      `clinical-record-card--${density}`,
      {
        'clinical-record-card--selected': selected,
        'clinical-record-card--masked': masked,
        'clinical-record-card--interactive': interactive,
        'focus-ring-inset': interactive && !masked,
      },
      cardClass,
    ]"
    v-bind="cardAttrs || undefined"
    :tabindex="interactive && !masked ? 0 : undefined"
    @click="interactive && !masked ? onCardClick($event) : undefined"
    @keydown="interactive && !masked ? onCardKeydown($event) : undefined"
  >
    <template v-if="masked">
      <div class="clinical-record-card__masked">
        <slot name="masked">
          {{ maskedMessage }}
        </slot>
      </div>
    </template>

    <template v-else>
      <header
        v-if="
          $slots.select ||
          $slots.identifier ||
          nodeRef ||
          identifier ||
          $slots.title ||
          title ||
          $slots.subtitle ||
          subtitle ||
          $slots.status ||
          status
        "
        class="clinical-record-card__header"
      >
        <div class="clinical-record-card__identity">
          <div v-if="$slots.select" class="clinical-record-card__select">
            <slot name="select" />
          </div>
          <div class="clinical-record-card__titles">
            <div
              v-if="$slots.identifier || nodeRef || identifier"
              class="clinical-record-card__identifier"
            >
              <slot name="identifier">
                <EntityLink
                  v-if="nodeRef"
                  :node-ref="nodeRef"
                  :label="identifier || undefined"
                  compact
                  tabular
                />
                <span v-else class="clinical-record-card__code">
                  {{ identifier }}
                </span>
              </slot>
            </div>
            <div
              v-if="$slots.title || title"
              class="clinical-record-card__title"
            >
              <slot name="title">
                {{ title }}
              </slot>
            </div>
            <div
              v-if="$slots.subtitle || subtitle"
              class="clinical-record-card__subtitle"
            >
              <slot name="subtitle">
                {{ subtitle }}
              </slot>
            </div>
          </div>
        </div>

        <div
          v-if="$slots.status || status"
          class="clinical-record-card__status"
        >
          <slot name="status">
            <span class="clinical-badge">{{ status }}</span>
          </slot>
        </div>
      </header>

      <dl v-if="fields.length > 0" class="clinical-record-card__grid">
        <div
          v-for="field in fields"
          :key="field.key"
          :class="[
            'clinical-record-card__field',
            { 'clinical-record-card__field--full': field.fullWidth },
          ]"
        >
          <dt class="clinical-record-card__label">
            {{ field.label }}
          </dt>
          <dd
            :class="[
              'clinical-record-card__value',
              {
                'clinical-record-card__value--mono': field.mono,
                'clinical-record-card__value--numeric':
                  field.align === 'right' || field.numeric,
              },
            ]"
          >
            <slot
              :name="'field-' + field.key"
              :field="field"
              :value="field.value"
            >
              {{
                field.value !== undefined &&
                field.value !== null &&
                field.value !== ""
                  ? field.value
                  : "—"
              }}
            </slot>
          </dd>
        </div>
      </dl>

      <div v-if="$slots.default" class="clinical-record-card__body">
        <slot />
      </div>

      <div v-if="$slots['after-body']" class="clinical-record-card__after-body">
        <slot name="after-body" />
      </div>

      <footer
        v-if="$slots.footer || $slots.actions"
        class="clinical-record-card__footer"
      >
        <slot name="footer">
          <slot name="actions" />
        </slot>
      </footer>
    </template>
  </article>
</template>

<style scoped>
.clinical-record-card {
  position: relative;
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
  padding: 14px;
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: var(--radius-md);
  box-shadow: var(--shadow-sm);
  color: var(--semantic-color-text);
  font-family: var(--semantic-font-family);
  font-variant-numeric: tabular-nums;
  font-variant-numeric: var(--font-variant-numeric-tabular);
  transition:
    background-color 0.15s ease,
    border-color 0.15s ease;
}

.clinical-record-card--compact {
  padding: 12px;
  gap: 10px;
}

.clinical-record-card--comfortable {
  padding: 14px;
  gap: 12px;
}

.clinical-record-card--touch {
  padding: 16px;
  gap: 14px;
}

.clinical-record-card--interactive {
  cursor: pointer;
}

.clinical-record-card--interactive:hover {
  border-color: var(--semantic-color-border-control);
}

.clinical-record-card--selected {
  background-color: var(--semantic-color-primary-light);
  border-color: var(--semantic-color-primary);
}

.clinical-record-card__masked {
  color: var(--semantic-color-text-muted);
  font-style: italic;
  font-size: var(--font-size-sm);
}

.clinical-record-card__header {
  position: relative;
  display: flex;
  flex-wrap: wrap;
  align-items: flex-start;
  justify-content: space-between;
  gap: 8px 12px;
  min-width: 0;
}

.clinical-record-card__identity {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  min-width: 0;
  flex: 1 1 160px;
}

.clinical-record-card__select {
  display: flex;
  align-items: center;
  padding-top: 2px;
  flex-shrink: 0;
}

.clinical-record-card__titles {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
  flex: 1;
}

.clinical-record-card__identifier {
  min-width: 0;
  font-family: var(--semantic-font-mono);
  font-size: var(--font-size-2xs);
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
  overflow-wrap: anywhere;
}

.clinical-record-card__code {
  font-family: var(--semantic-font-mono);
  font-variant-numeric: tabular-nums;
  font-variant-numeric: var(--font-variant-numeric-tabular);
}

.clinical-record-card__title {
  font-size: 0.9375rem;
  font-weight: 600;
  line-height: 1.35;
  color: var(--semantic-color-text);
  overflow-wrap: anywhere;
}

.clinical-record-card--touch .clinical-record-card__title {
  font-size: 1rem;
}

.clinical-record-card__subtitle {
  font-size: var(--font-size-xs);
  color: var(--semantic-color-text-secondary);
  overflow-wrap: anywhere;
}

.clinical-record-card__status {
  position: relative;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: flex-end;
  gap: 6px;
  max-width: 100%;
  flex-shrink: 0;
}

.clinical-record-card__grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px 12px;
  margin: 0;
  padding: 10px 12px;
  background-color: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  border-radius: var(--radius-sm);
  min-width: 0;
}

@media (max-width: 359px) {
  .clinical-record-card__grid {
    grid-template-columns: minmax(0, 1fr);
  }
}

.clinical-record-card__field {
  position: relative;
  display: flex;
  flex-direction: column;
  gap: 3px;
  min-width: 0;
}

.clinical-record-card__field--full {
  grid-column: 1 / -1;
}

.clinical-record-card__label {
  margin: 0;
  font-size: var(--font-size-2xs);
  font-weight: 600;
  letter-spacing: 0.03em;
  text-transform: uppercase;
  color: var(--semantic-color-text-secondary);
  overflow-wrap: anywhere;
}

.clinical-record-card__value {
  margin: 0;
  min-width: 0;
  font-size: 0.875rem;
  font-weight: 500;
  line-height: 1.4;
  color: var(--semantic-color-text);
  font-variant-numeric: tabular-nums;
  font-variant-numeric: var(--font-variant-numeric-tabular);
  overflow-wrap: anywhere;
}

.clinical-record-card--touch .clinical-record-card__value {
  font-size: 0.9375rem;
}

.clinical-record-card__value--mono {
  font-family: var(--semantic-font-mono);
}

.clinical-record-card__value--numeric {
  font-variant-numeric: tabular-nums;
  font-variant-numeric: var(--font-variant-numeric-tabular);
}

.clinical-record-card__body,
.clinical-record-card__after-body {
  position: relative;
  min-width: 0;
  max-width: 100%;
}

.clinical-record-card__footer {
  position: relative;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: flex-end;
  gap: 8px;
  min-width: 0;
  max-width: 100%;
  padding-top: 8px;
  border-top: 1px solid var(--semantic-color-border);
}

.clinical-record-card__footer
  :deep(:is(button, a, [role="button"]):not(.sr-only):not(.visually-hidden)) {
  min-height: 44px;
  min-width: 44px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  box-sizing: border-box;
}

.clinical-record-card--touch
  .clinical-record-card__footer
  :deep(:is(button, a, [role="button"]):not(.sr-only):not(.visually-hidden)) {
  min-height: var(--semantic-touch-target-min, 48px);
  min-width: var(--semantic-touch-target-min, 48px);
  width: 100%;
}
</style>
