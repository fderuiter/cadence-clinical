<template>
  <component
    :is="routerLinkComponent ?? 'a'"
    v-bind="
      routerLinkComponent ? { to: '/plans' } : { href: fallbackPlansHref }
    "
  >
    <slot>plans &amp; product-family roadmap</slot>
  </component>
</template>

<script setup lang="ts">
import { getCurrentInstance } from "vue";

/**
 * Renders a router-aware link to `/plans` when `RouterLink` is registered (preserving both web-history
 * and static demo hash-history subpath routing per ADR-2550), falling back to a `BASE_URL`-prefixed
 * anchor in standalone unit-test mounts.
 */
const instance = getCurrentInstance();
const routerLinkComponent = instance?.appContext.components.RouterLink;
const fallbackPlansHref = `${(import.meta.env.BASE_URL || "/").replace(/\/?$/, "/")}plans`;
</script>
