<template>
  <div
    class="site-contact-links"
    :class="{ 'site-contact-compact': compact }"
    role="region"
    :aria-label="regionLabel"
  >
    <div class="site-contact-header">
      <span class="site-contact-title">{{ heading }}</span>
      <span v-if="!compact" class="site-contact-subtitle">{{
        contact.siteName
      }}</span>
    </div>
    <div class="site-contact-actions">
      <a
        :href="contact.phoneHref"
        class="site-contact-link site-contact-phone touch-target-interactive"
        :aria-label="phoneAriaLabel"
      >
        <span aria-hidden="true">
          <ClinicalIcon name="phone" :size="16" />
        </span>
        <span>{{ phoneVisibleLabel }}</span>
      </a>
      <a
        :href="contact.emailHref"
        class="site-contact-link site-contact-email touch-target-interactive"
        :aria-label="emailAriaLabel"
      >
        <span aria-hidden="true">
          <ClinicalIcon name="mail" :size="16" />
        </span>
        <span>{{ emailVisibleLabel }}</span>
      </a>
    </div>
  </div>
</template>

<script setup>
import { computed } from "vue";
import { ClinicalIcon } from "ui";

import { getSiteSupportContact } from "../features/site-support.js";
import { useUiStore } from "../stores/ui.js";

const props = defineProps({
  compact: {
    type: Boolean,
    default: false,
  },
  heading: {
    type: String,
    default: "Study Coordinator Support",
  },
  regionLabel: {
    type: String,
    default: "Study site coordinator contact options",
  },
});

const ui = useUiStore();
const contact = computed(
  () => ui.siteSupportContact || getSiteSupportContact()
);

const phoneVisibleLabel = computed(() =>
  props.compact ? "Call Site" : `Call ${contact.value.phoneDisplay}`
);

const phoneAriaLabel = computed(() =>
  props.compact
    ? `Call Site (${contact.value.coordinatorRole} ${contact.value.phoneDisplay})`
    : `Call ${contact.value.phoneDisplay} (${contact.value.coordinatorRole})`
);

const emailVisibleLabel = computed(() =>
  props.compact ? "Email Site" : "Email Coordinator"
);

const emailAriaLabel = computed(() =>
  props.compact
    ? `Email Site (${contact.value.coordinatorRole} ${contact.value.emailDisplay})`
    : `Email Coordinator (${contact.value.emailDisplay})`
);
</script>
