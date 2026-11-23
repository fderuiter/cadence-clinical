<script setup>
import { computed } from "vue";
import NotFound from "vitepress/dist/client/theme-default/NotFound.vue";
import { useData } from "vitepress/dist/client/theme-default/composables/data";
import { useSidebar } from "vitepress/dist/client/theme-default/composables/sidebar";
import VPDoc from "vitepress/dist/client/theme-default/components/VPDoc.vue";
import VPHome from "vitepress/dist/client/theme-default/components/VPHome.vue";
import VPPage from "vitepress/dist/client/theme-default/components/VPPage.vue";

const { page, frontmatter } = useData();
const { hasSidebar } = useSidebar();

const isDocLayout = computed(
  () =>
    !page.value.isNotFound &&
    (!frontmatter.value.layout || frontmatter.value.layout === "doc")
);
</script>

<template>
  <component
    :is="isDocLayout ? 'div' : 'main'"
    id="VPContent"
    class="VPContent"
    :class="{
      'has-sidebar': hasSidebar,
      'is-home': frontmatter.layout === 'home',
    }"
  >
    <slot v-if="page.isNotFound" name="not-found"><NotFound /></slot>

    <VPPage v-else-if="frontmatter.layout === 'page'">
      <template #page-top><slot name="page-top" /></template>
      <template #page-bottom><slot name="page-bottom" /></template>
    </VPPage>

    <VPHome v-else-if="frontmatter.layout === 'home'">
      <template #home-hero-before><slot name="home-hero-before" /></template>
      <template #home-hero-info-before>
        <slot name="home-hero-info-before" />
      </template>
      <template #home-hero-info><slot name="home-hero-info" /></template>
      <template #home-hero-info-after>
        <slot name="home-hero-info-after" />
      </template>
      <template #home-hero-actions-after>
        <slot name="home-hero-actions-after" />
      </template>
      <template #home-hero-image><slot name="home-hero-image" /></template>
      <template #home-hero-after><slot name="home-hero-after" /></template>
      <template #home-features-before>
        <slot name="home-features-before" />
      </template>
      <template #home-features-after>
        <slot name="home-features-after" />
      </template>
    </VPHome>

    <component
      :is="frontmatter.layout"
      v-else-if="frontmatter.layout && frontmatter.layout !== 'doc'"
    />

    <VPDoc v-else>
      <template #doc-top><slot name="doc-top" /></template>
      <template #doc-bottom><slot name="doc-bottom" /></template>

      <template #doc-footer-before><slot name="doc-footer-before" /></template>
      <template #doc-before><slot name="doc-before" /></template>
      <template #doc-after><slot name="doc-after" /></template>

      <template #aside-top><slot name="aside-top" /></template>
      <template #aside-outline-before>
        <slot name="aside-outline-before" />
      </template>
      <template #aside-outline-after>
        <slot name="aside-outline-after" />
      </template>
      <template #aside-ads-before><slot name="aside-ads-before" /></template>
      <template #aside-ads-after><slot name="aside-ads-after" /></template>
      <template #aside-bottom><slot name="aside-bottom" /></template>
    </VPDoc>
  </component>
</template>
