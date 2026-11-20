<script setup>
import { computed } from "vue";
import { useRoute } from "vitepress";
import {
  formatDisplayDate,
  normalizeIsoDate,
  normalizeRelatedLinks,
  resolveBaseHref,
} from "../../blogShared.mjs";

const route = useRoute();
const frontmatter = computed(() => route.data?.frontmatter ?? {});

const isBlogPost = computed(() => {
  const path = route.path || "";
  if (!path.includes("/blog/")) return false;
  if (path.endsWith("/blog/") || path.endsWith("/blog/index.html")) {
    return false;
  }
  return Boolean(frontmatter.value?.date || frontmatter.value?.summary);
});

const isoDate = computed(() => normalizeIsoDate(frontmatter.value?.date));
const displayDate = computed(() => formatDisplayDate(isoDate.value));

const authors = computed(() => {
  const raw = frontmatter.value?.authors;
  if (Array.isArray(raw) && raw.length > 0) {
    return raw.map((a) => String(a).trim()).filter(Boolean);
  }
  if (frontmatter.value?.author) {
    return [String(frontmatter.value.author).trim()];
  }
  return ["@fderuiter"];
});

const summary = computed(() =>
  String(
    frontmatter.value?.summary || frontmatter.value?.description || ""
  ).trim()
);

const tags = computed(() => {
  const raw = frontmatter.value?.tags;
  if (!Array.isArray(raw)) return [];
  return raw.map((t) => String(t).trim().toLowerCase()).filter(Boolean);
});

const related = computed(() =>
  normalizeRelatedLinks(frontmatter.value?.related)
);

function resolveHref(link) {
  return resolveBaseHref(link);
}
</script>

<template>
  <header v-if="isBlogPost" class="blog-post-header">
    <div class="blog-post-eyebrow">
      <a class="blog-back-link" :href="resolveHref('/blog/')">
        ← Engineering Blog
      </a>
      <a
        class="blog-feed-inline"
        :href="resolveHref('/feed.xml')"
        target="_blank"
        rel="noopener"
      >
        RSS Feed
      </a>
    </div>

    <div class="blog-card-meta">
      <time :datetime="isoDate" class="blog-date">{{ displayDate }}</time>
      <span class="blog-meta-sep" aria-hidden="true">·</span>
      <span class="blog-authors">By {{ authors.join(", ") }}</span>
    </div>

    <p v-if="summary" class="blog-post-lead">{{ summary }}</p>

    <div v-if="tags.length > 0" class="blog-card-tags">
      <a
        v-for="tag in tags"
        :key="tag"
        class="blog-card-tag"
        :href="`${resolveHref('/blog/')}?tag=${encodeURIComponent(tag)}`"
      >
        #{{ tag }}
      </a>
    </div>

    <div v-if="related.length > 0" class="blog-card-related">
      <span class="blog-related-label">Cited records:</span>
      <a
        v-for="item in related"
        :key="item.link"
        class="blog-related-link"
        :href="resolveHref(item.link)"
      >
        {{ item.text }}
      </a>
    </div>
  </header>
</template>
