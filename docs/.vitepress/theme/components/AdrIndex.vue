<script setup>
import { computed, onMounted, ref } from "vue";
import { data as adrs } from "../../../adr/adrs.data.mjs";
import {
  ADR_DOMAINS,
  filterAdrEntries,
  groupAdrEntriesByDomain,
} from "../../adrShared.mjs";
import { resolveBaseHref } from "../../blogShared.mjs";

const selectedDomain = ref("all");
const searchQuery = ref("");

const domainSummaries = computed(() => {
  const counts = new Map();
  for (const adr of adrs) {
    counts.set(adr.domainSlug, (counts.get(adr.domainSlug) ?? 0) + 1);
  }
  return ADR_DOMAINS.map((domain) => ({
    ...domain,
    count: counts.get(domain.slug) ?? 0,
  })).filter((domain) => domain.count > 0);
});

const filteredAdrs = computed(() =>
  filterAdrEntries(adrs, {
    domainSlug: selectedDomain.value,
    query: searchQuery.value,
  })
);

const groupedAdrs = computed(() => groupAdrEntriesByDomain(filteredAdrs.value));

function resolveHref(link) {
  return resolveBaseHref(link);
}

function syncUrlState() {
  if (typeof window === "undefined") return;
  const url = new URL(window.location.href);
  if (selectedDomain.value === "all") {
    url.searchParams.delete("domain");
  } else {
    url.searchParams.set("domain", selectedDomain.value);
  }
  const trimmedQuery = searchQuery.value.trim();
  if (!trimmedQuery) {
    url.searchParams.delete("q");
  } else {
    url.searchParams.set("q", trimmedQuery);
  }
  window.history.replaceState({}, "", url.toString());
}

function selectDomain(slug) {
  const normalized = String(slug ?? "all")
    .trim()
    .toLowerCase();
  selectedDomain.value = normalized || "all";
  syncUrlState();
}

function onQueryInput(event) {
  searchQuery.value = event.target.value;
  syncUrlState();
}

onMounted(() => {
  if (typeof window === "undefined") return;
  const params = new URLSearchParams(window.location.search);
  const requestedDomain = params.get("domain")?.trim().toLowerCase();
  if (
    requestedDomain &&
    domainSummaries.value.some((domain) => domain.slug === requestedDomain)
  ) {
    selectedDomain.value = requestedDomain;
  }
  const requestedQuery = params.get("q")?.trim();
  if (requestedQuery) {
    searchQuery.value = requestedQuery;
  }
});
</script>

<template>
  <section class="adr-index" aria-label="Architectural decision records">
    <div class="adr-toolbar">
      <p class="adr-count" role="status" aria-live="polite">
        Showing <strong class="adr-num">{{ filteredAdrs.length }}</strong> of
        <strong class="adr-num">{{ adrs.length }}</strong> architectural
        decision records
      </p>
      <label for="adr-search-input" class="adr-search-label">
        <span class="adr-search-caption">Filter by ID or keyword</span>
        <input
          id="adr-search-input"
          type="search"
          class="adr-search-input"
          :value="searchQuery"
          placeholder="e.g. ADR-2250, bitemporal, gateway"
          @input="onQueryInput"
        />
      </label>
    </div>

    <div
      class="adr-domain-filter"
      role="region"
      aria-label="Filter decisions by functional domain"
    >
      <button
        type="button"
        class="blog-tag-pill"
        :class="{ active: selectedDomain === 'all' }"
        :aria-pressed="selectedDomain === 'all'"
        @click="selectDomain('all')"
      >
        All domains (<span class="adr-num">{{ adrs.length }}</span
        >)
      </button>
      <button
        v-for="domain in domainSummaries"
        :key="domain.slug"
        type="button"
        class="blog-tag-pill"
        :class="{ active: selectedDomain === domain.slug }"
        :aria-pressed="selectedDomain === domain.slug"
        @click="selectDomain(domain.slug)"
      >
        {{ domain.shortLabel }} (<span class="adr-num">{{ domain.count }}</span
        >)
      </button>
    </div>

    <p v-if="groupedAdrs.length === 0" class="blog-empty-state">
      No architectural decision records match the current filter.
    </p>

    <div v-else class="adr-domain-groups">
      <section
        v-for="group in groupedAdrs"
        :key="group.slug"
        class="adr-domain-section"
        :aria-labelledby="`adr-domain-${group.slug}`"
      >
        <h3 :id="`adr-domain-${group.slug}`" class="adr-domain-heading">
          <span>{{ group.label }}</span>
          <span class="adr-domain-badge adr-num">{{ group.items.length }}</span>
        </h3>

        <ul class="adr-record-list">
          <li v-for="adr in group.items" :key="adr.slug" class="adr-record-row">
            <div class="adr-record-main">
              <code class="adr-code">{{ adr.code }}</code>
              <a :href="resolveHref(adr.url)" class="adr-record-link">{{
                adr.title
              }}</a>
            </div>
            <div class="adr-record-meta">
              <span
                v-if="adr.isSuperseded"
                class="adr-status-pill adr-status-superseded"
                >{{ adr.status }}</span
              >
              <span
                v-if="adr.alsoKnownAs"
                class="adr-aka-note"
                :title="`Also known as ${adr.alsoKnownAs}`"
                >Formerly {{ adr.alsoKnownAs }}</span
              >
              <time
                v-if="adr.date"
                :datetime="adr.date"
                class="adr-date adr-num"
                >{{ adr.date }}</time
              >
            </div>
          </li>
        </ul>
      </section>
    </div>
  </section>
</template>
