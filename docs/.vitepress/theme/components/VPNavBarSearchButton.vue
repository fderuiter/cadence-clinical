<script setup>
import { createSearchTranslate } from "vitepress/dist/client/theme-default/support/translation";

const defaultTranslations = {
  button: {
    buttonText: "Search",
  },
};

const translate = createSearchTranslate(defaultTranslations);
</script>

<template>
  <button type="button" class="DocSearch DocSearch-Button">
    <span class="DocSearch-Button-Container">
      <span class="vp-icon DocSearch-Search-Icon" aria-hidden="true" />
      <span class="DocSearch-Button-Placeholder">{{
        translate("button.buttonText")
      }}</span>
    </span>
    <span class="DocSearch-Button-Keys" aria-hidden="true">
      <kbd class="DocSearch-Button-Key" />
      <kbd class="DocSearch-Button-Key">K</kbd>
    </span>
  </button>
</template>
