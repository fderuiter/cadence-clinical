<script setup>
import { computed, onMounted, ref } from "vue";
import { data as posts } from "../../../blog/posts.data.mjs";
import {
  filterBlogPostsByTag,
  resolveBaseHref,
  resolveBlogLinkRel,
  resolveBlogLinkTarget,
} from "../../blogShared.mjs";

const selectedTag = ref("all");

const tagSummaries = computed(() => {
  const counts = new Map();
  for (const post of posts) {
    for (const tag of post.tags ?? []) {
      counts.set(tag, (counts.get(tag) ?? 0) + 1);
    }
  }
  return Array.from(counts.entries())
    .map(([tag, count]) => ({ tag, count }))
    .sort((a, b) => a.tag.localeCompare(b.tag));
});

const filteredPosts = computed(() =>
  filterBlogPostsByTag(posts, selectedTag.value)
);

function resolveHref(link) {
  return resolveBaseHref(link);
}

const resolveLinkTarget = resolveBlogLinkTarget;
const resolveLinkRel = resolveBlogLinkRel;

function selectTag(tag) {
  const normalized = String(tag ?? "all")
    .trim()
    .toLowerCase();
  selectedTag.value = normalized || "all";

  if (typeof window !== "undefined") {
    const url = new URL(window.location.href);
    if (selectedTag.value === "all") {
      url.searchParams.delete("tag");
    } else {
      url.searchParams.set("tag", selectedTag.value);
    }
    window.history.replaceState({}, "", url.toString());
  }
}

onMounted(() => {
  if (typeof window === "undefined") return;
  const params = new URLSearchParams(window.location.search);
  const requested = params.get("tag")?.trim().toLowerCase();
  if (
    requested &&
    tagSummaries.value.some((entry) => entry.tag === requested)
  ) {
    selectedTag.value = requested;
  }
});
</script>

<template>
  <section class="blog-index" aria-label="Engineering blog posts">
    <div class="blog-toolbar">
      <p class="blog-count">
        Showing <strong>{{ filteredPosts.length }}</strong> of
        <strong>{{ posts.length }}</strong> published milestone posts
      </p>
      <a
        class="blog-rss-button"
        :href="resolveHref('/feed.xml')"
        target="_blank"
        rel="noopener"
      >
        Subscribe via RSS (XML)
      </a>
    </div>

    <div
      class="blog-tag-filter"
      role="region"
      aria-label="Filter posts by topic tag"
    >
      <button
        type="button"
        class="blog-tag-pill tabular-nums"
        :class="{ active: selectedTag === 'all' }"
        :aria-pressed="selectedTag === 'all'"
        @click="selectTag('all')"
      >
        All topics ({{ posts.length }})
      </button>
      <button
        v-for="item in tagSummaries"
        :key="item.tag"
        type="button"
        class="blog-tag-pill"
        :class="{ active: selectedTag === item.tag }"
        :aria-pressed="selectedTag === item.tag"
        @click="selectTag(item.tag)"
      >
        #{{ item.tag }} ({{ item.count }})
      </button>
    </div>

    <p v-if="filteredPosts.length === 0" class="blog-empty-state">
      No published posts match the selected topic tag.
    </p>

    <div v-else class="blog-post-list">
      <article
        v-for="post in filteredPosts"
        :key="post.url"
        class="blog-post-card"
      >
        <div class="blog-card-meta">
          <time :datetime="post.date" class="blog-date">{{
            post.displayDate
          }}</time>
          <span class="blog-meta-sep" aria-hidden="true">·</span>
          <span class="blog-authors">By {{ post.authors.join(", ") }}</span>
        </div>

        <h2 class="blog-card-title">
          <a :href="resolveHref(post.url)">{{ post.title }}</a>
        </h2>

        <p class="blog-card-summary">{{ post.summary }}</p>

        <div v-if="post.tags.length > 0" class="blog-card-tags">
          <button
            v-for="tag in post.tags"
            :key="tag"
            type="button"
            class="blog-card-tag"
            :aria-label="`Filter posts by tag ${tag}`"
            @click="selectTag(tag)"
          >
            #{{ tag }}
          </button>
        </div>

        <div v-if="post.related.length > 0" class="blog-card-related">
          <span class="blog-related-label">Related decisions:</span>
          <a
            v-for="rel in post.related"
            :key="rel.link"
            class="blog-related-link"
            :href="resolveHref(rel.link)"
            :target="resolveLinkTarget(rel.link)"
            :rel="resolveLinkRel(rel.link)"
          >
            {{ rel.text }}
          </a>
        </div>
      </article>
    </div>
  </section>
</template>
