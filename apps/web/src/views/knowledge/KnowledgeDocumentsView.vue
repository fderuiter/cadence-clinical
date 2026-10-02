<template>
  <div id="sop-prose-viewer" class="reader-body-card">
    <div
      v-if="selectedArticle && selectedArticle.body_html"
      class="prose-content"
      v-html="selectedArticle.body_html"
    />
    <div
      v-else-if="selectedArticle && selectedArticle.body_markdown"
      class="prose-content markdown-fallback"
    >
      <pre>{{ selectedArticle.body_markdown }}</pre>
    </div>
    <div v-else class="prose-empty">
      <p>No content has been authored for this SOP draft.</p>
    </div>
  </div>
</template>

<script setup>
defineProps({
  selectedArticle: {
    type: Object,
    default: null,
  },
});
</script>
