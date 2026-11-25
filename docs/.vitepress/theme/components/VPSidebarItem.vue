<script setup>
import { computed } from "vue";
import { useSidebarControl } from "vitepress/dist/client/theme-default/composables/sidebar";
import VPLink from "vitepress/dist/client/theme-default/components/VPLink.vue";

const props = defineProps({
  item: {
    type: Object,
    required: true,
  },
  depth: {
    type: Number,
    required: true,
  },
});

const {
  collapsed,
  collapsible,
  isLink,
  isActiveLink,
  hasActiveLink,
  hasChildren,
  toggle,
} = useSidebarControl(computed(() => props.item));

const sectionTag = computed(() => (hasChildren.value ? "section" : "div"));

const linkTag = computed(() => (isLink.value ? "a" : "div"));

const textTag = computed(() => {
  return !hasChildren.value
    ? "p"
    : props.depth + 2 === 7
      ? "p"
      : `h${props.depth + 2}`;
});

const hasCollapsibleCaret = computed(() =>
  Boolean(
    props.item.collapsed != null && props.item.items && props.item.items.length
  )
);

const classes = computed(() => [
  [`level-${props.depth}`],
  { collapsible: collapsible.value },
  { collapsed: collapsed.value },
  { "is-link": isLink.value },
  { "is-active": isActiveLink.value },
  { "has-active": hasActiveLink.value },
]);

function onCaretClick() {
  if (props.item.link) {
    toggle();
  }
}
</script>

<template>
  <component :is="sectionTag" class="VPSidebarItem" :class="classes">
    <button
      v-if="item.text && !item.link && collapsible"
      type="button"
      class="item item-toggle"
      :aria-expanded="!collapsed"
      @click="toggle"
    >
      <span class="indicator" aria-hidden="true" />
      <span class="text">
        {{ item.text }}
        <span v-if="item.badge" class="VPBadge" :class="item.badge.variant">{{
          item.badge.text
        }}</span>
      </span>
      <span v-if="hasCollapsibleCaret" class="caret" aria-hidden="true">
        <span class="vpi-chevron-right caret-icon" />
      </span>
    </button>

    <div v-else-if="item.text" class="item">
      <div class="indicator" />

      <VPLink
        v-if="item.link"
        :tag="linkTag"
        class="link"
        :href="item.link"
        :rel="item.rel"
        :target="item.target"
      >
        <component :is="textTag" class="text">
          {{ item.text }}
          <span v-if="item.badge" class="VPBadge" :class="item.badge.variant">{{
            item.badge.text
          }}</span>
        </component>
      </VPLink>
      <component :is="textTag" v-else class="text">
        {{ item.text }}
        <span v-if="item.badge" class="VPBadge" :class="item.badge.variant">{{
          item.badge.text
        }}</span>
      </component>

      <button
        v-if="item.link && hasCollapsibleCaret"
        type="button"
        class="caret"
        :aria-expanded="!collapsed"
        :aria-label="`Toggle ${item.text} section`"
        @click="onCaretClick"
      >
        <span class="vpi-chevron-right caret-icon" aria-hidden="true" />
      </button>
    </div>

    <div v-if="item.items && item.items.length" class="items">
      <template v-if="depth < 5">
        <VPSidebarItem
          v-for="i in item.items"
          :key="i.text"
          :item="i"
          :depth="depth + 1"
        />
      </template>
    </div>
  </component>
</template>
