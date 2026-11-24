<script setup>
import { computed } from "vue";
import { useRoute } from "vitepress";
import { data as posts } from "../../../blog/posts.data.mjs";
import {
  normalizeRelatedLinks,
  resolveBaseHref,
  resolveBlogLinkRel,
  resolveBlogLinkTarget,
} from "../../blogShared.mjs";

const route = useRoute();
const frontmatter = computed(() => route.data?.frontmatter ?? {});

const currentSlug = computed(() => {
  const path = route.path || "";
  if (!path.includes("/blog/")) return "";
  if (path.endsWith("/blog/") || path.endsWith("/blog/index.html")) return "";
  const parts = path.split("/");
  const last = parts[parts.length - 1] || "";
  return last.replace(/\.(?:html|md)$/i, "");
});

const currentIndex = computed(() => {
  if (!currentSlug.value) return -1;
  return posts.findIndex((p) => p.slug === currentSlug.value);
});

const isBlogPost = computed(
  () =>
    Boolean(currentSlug.value) &&
    (currentIndex.value >= 0 || Boolean(frontmatter.value?.date))
);

const newerPost = computed(() =>
  currentIndex.value > 0 ? posts[currentIndex.value - 1] : null
);

const olderPost = computed(() =>
  currentIndex.value >= 0 && currentIndex.value + 1 < posts.length
    ? posts[currentIndex.value + 1]
    : null
);

const related = computed(() =>
  normalizeRelatedLinks(frontmatter.value?.related)
);

function resolveHref(link) {
  return resolveBaseHref(link);
}
</script>

<template>
  <footer v-if="isBlogPost" class="blog-post-footer">
    <section
      v-if="related.length > 0"
      class="blog-related-panel"
      aria-label="Related decisions and specifications"
    >
      <h2 class="blog-related-heading">
        Related Decisions &amp; Specifications
      </h2>
      <ul class="blog-related-list">
        <li v-for="item in related" :key="item.link">
          <a
            :href="resolveHref(item.link)"
            :target="resolveBlogLinkTarget(item.link)"
            :rel="resolveBlogLinkRel(item.link)"
            >{{ item.text }}</a
          >
        </li>
      </ul>
    </section>

    <nav
      v-if="newerPost || olderPost"
      class="blog-chronological-nav"
      aria-label="Chronological blog post navigation"
    >
      <a
        v-if="newerPost"
        class="blog-chrono-card"
        :href="resolveHref(newerPost.url)"
      >
        <span class="blog-chrono-label"
          >← Newer Milestone ({{ newerPost.displayDate }})</span
        >
        <span class="blog-chrono-title">{{ newerPost.title }}</span>
      </a>
      <div v-else class="blog-chrono-spacer" />

      <a
        v-if="olderPost"
        class="blog-chrono-card blog-chrono-older"
        :href="resolveHref(olderPost.url)"
      >
        <span class="blog-chrono-label"
          >Older Milestone ({{ olderPost.displayDate }}) →</span
        >
        <span class="blog-chrono-title">{{ olderPost.title }}</span>
      </a>
    </nav>
  </footer>
</template>
