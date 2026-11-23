<template>
  <section
    id="stage-overview"
    class="stage-overview"
    aria-labelledby="stage-overview-title"
  >
    <h2 id="stage-overview-title" tabindex="-1">
      {{ stage.label }}
    </h2>
    <p class="stage-scope">{{ stage.scope }}.</p>
    <ul v-if="stage.sections.length" class="stage-sections">
      <li v-for="item in stage.sections" :key="item.id">
        <router-link :to="sectionLocation(item.id)">
          {{ item.label }}
        </router-link>
        <span class="stage-section-description"> {{ item.description }}.</span>
      </li>
    </ul>
    <p id="stage-empty" class="stage-empty">
      {{
        stage.sections.length
          ? `The rest of ${stage.label} arrives as its services are built.`
          : `No part of ${stage.label} has a service yet.`
      }}
      <router-link to="/plans">
        Compare product families &amp; pre-release roadmap </router-link
      >.
    </p>
  </section>
</template>

<script setup lang="ts">
import type { RouteLocationRaw } from "vue-router";
import type { Stage } from "../../canvas/lifecycle";

defineProps<{
  stage: Stage;
  sectionLocation: (sectionId: string) => RouteLocationRaw;
}>();
</script>

<style scoped>
.stage-overview h2 {
  margin: 0 0 4px;
  font-size: 1rem;
}

.stage-scope,
.stage-empty,
.stage-section-description {
  color: var(--neutral-dark);
}

.stage-sections {
  padding-left: 18px;
}
</style>
