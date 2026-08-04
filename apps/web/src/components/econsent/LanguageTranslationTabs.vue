<template>
  <div class="language-translation-tabs-container card">
    <div class="tabs-header">
      <span class="tabs-label">🌍 Localized Consent Translations:</span>
      <div class="tabs-list" role="tablist">
        <button
          v-for="lang in availableLanguages"
          :key="lang.code"
          type="button"
          role="tab"
          :aria-selected="econsentStore.activeLanguage === lang.code"
          :class="[
            'tab-item',
            { active: econsentStore.activeLanguage === lang.code },
          ]"
          @click="selectLanguage(lang.code)"
        >
          <span class="flag-icon">{{ lang.flag }}</span>
          <span class="lang-name">{{ lang.name }}</span>
          <span class="lang-code-badge">{{ lang.code.toUpperCase() }}</span>
        </button>
      </div>
    </div>

    <!-- Active Language Banner/Info -->
    <div class="active-lang-banner">
      <p>
        Currently editing content in <strong>{{ activeLanguageName }}</strong
        >. All modifications to the section content will be saved under the
        <strong>{{ econsentStore.activeLanguage }}</strong> translation locale.
      </p>
      <div class="completion-metrics">
        <span class="metric-badge">
          Sections: {{ translatedSectionsCount }} /
          {{ econsentStore.sections.length }} Translated
        </span>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed } from "vue";
import { useEconsentStore } from "../../stores/econsent.js";

const econsentStore = useEconsentStore();

const availableLanguages = [
  { code: "en", name: "English", flag: "🇬🇧" },
  { code: "es", name: "Spanish", flag: "🇪🇸" },
  { code: "fr", name: "French", flag: "🇫🇷" },
  { code: "de", name: "German", flag: "🇩🇪" },
];

const activeLanguageName = computed(() => {
  const found = availableLanguages.find(
    (l) => l.code === econsentStore.activeLanguage
  );
  return found ? found.name : econsentStore.activeLanguage;
});

const translatedSectionsCount = computed(() => {
  const currentLang = econsentStore.activeLanguage;
  return econsentStore.sections.filter((sec) => {
    return (
      sec.translations &&
      sec.translations[currentLang] &&
      sec.translations[currentLang].html.trim() !== ""
    );
  }).length;
});

const selectLanguage = (langCode) => {
  econsentStore.setLanguage(langCode);
};
</script>

<style scoped>
.language-translation-tabs-container {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-sm);
  background-color: var(--color-surface);
  padding: var(--spacing-sm) var(--spacing-md);
  border: 1px solid var(--color-border);
  border-radius: 8px;
}

.tabs-header {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: var(--spacing-sm);
}

.tabs-label {
  font-weight: 600;
  font-size: 0.9rem;
  color: var(--color-text);
}

.tabs-list {
  display: flex;
  gap: var(--spacing-xs);
}

.tab-item {
  display: flex;
  align-items: center;
  gap: var(--spacing-2xs);
  padding: var(--spacing-2xs) var(--spacing-sm);
  font-size: 0.85rem;
  background-color: var(--color-surface-muted);
  border: 1px solid var(--color-border);
  border-radius: 6px;
  cursor: pointer;
  transition: all 0.2s;
}

.tab-item:hover {
  background-color: var(--color-surface-muted);
  border-color: var(--color-accent);
}

.tab-item.active {
  background-color: var(--color-primary-light);
  border-color: var(--color-accent);
  color: var(--color-primary);
  box-shadow: 0 1px 3px rgba(59, 130, 246, 0.1);
  font-weight: 600;
}

.flag-icon {
  font-size: 1.1rem;
}

.lang-code-badge {
  font-size: 0.7rem;
  background-color: var(--color-surface-muted);
  padding: var(--spacing-2xs) var(--spacing-2xs);
  border-radius: 3px;
  color: var(--color-text-muted);
}

.active-lang-banner {
  background-color: var(--color-surface-muted);
  border-left: 3px solid var(--color-accent);
  padding: var(--spacing-sm) var(--spacing-sm);
  border-radius: 0 6px 6px 0;
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: var(--spacing-sm);
}

.active-lang-banner p {
  margin: 0;
  font-size: 0.8rem;
  color: var(--color-text-muted);
  line-height: 1.4;
  flex: 1;
}

.completion-metrics {
  display: flex;
}

.metric-badge {
  font-size: 0.75rem;
  background-color: var(--color-success-bg);
  color: var(--color-success);
  border: 1px solid var(--color-success-bg);
  padding: var(--spacing-2xs) var(--spacing-xs);
  border-radius: 9999px;
  font-weight: 600;
}
</style>
