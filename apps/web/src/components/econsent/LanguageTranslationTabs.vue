<template>
  <div class="language-translation-tabs-container card">
    <div class="tabs-header">
      <span class="tabs-label"
        >🌍 {{ $t("econsent.translationTabs.headerLabel") }}</span
      >
      <div
        ref="tabsListRef"
        class="tabs-list"
        role="tablist"
        aria-label="Consent translation languages"
      >
        <button
          v-for="lang in availableLanguages"
          :key="lang.code"
          type="button"
          role="tab"
          :aria-selected="econsentStore.activeLanguage === lang.code"
          :tabindex="econsentStore.activeLanguage === lang.code ? 0 : -1"
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
        {{
          $t("econsent.translationTabs.bannerText", {
            language: activeLanguageName,
            locale: econsentStore.activeLanguage,
          })
        }}
      </p>
      <div class="completion-metrics">
        <span class="metric-badge">
          {{
            $t("econsent.translationTabs.sectionsMetric", {
              translated: translatedSectionsCount,
              total: econsentStore.sections.length,
            })
          }}
        </span>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed, ref } from "vue";
import { useEconsentStore } from "../../stores/econsent.js";
import { useRovingTabindex } from "ui";
import { setGlobalLocale } from "../../i18n/index.js";

const econsentStore = useEconsentStore();
const tabsListRef = ref(null);

const availableLanguages = [
  { code: "en", name: "English", flag: "🇬🇧" },
  { code: "es", name: "Spanish", flag: "🇪🇸" },
  { code: "fr", name: "French", flag: "🇫🇷" },
  { code: "de", name: "German", flag: "🇩🇪" },
];

const onSelectTab = (index) => {
  const selectedLang = availableLanguages[index];
  if (selectedLang) {
    selectLanguage(selectedLang.code);
  }
};

useRovingTabindex(tabsListRef, onSelectTab);

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
  setGlobalLocale(langCode);
};
</script>

<style scoped>
.language-translation-tabs-container {
  display: flex;
  flex-direction: column;
  gap: 12px;
  background-color: white;
  padding: 12px 16px;
  border: 1px solid var(--border);
  border-radius: 8px;
}

.tabs-header {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
}

.tabs-label {
  font-weight: 600;
  font-size: 0.9rem;
  color: var(--neutral-dark);
}

.tabs-list {
  display: flex;
  gap: 8px;
}

.tab-item {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 6px 12px;
  font-size: 0.85rem;
  background-color: var(--neutral-light);
  border: 1px solid var(--border);
  border-radius: 6px;
  cursor: pointer;
  transition: all 0.2s;
}

.tab-item:hover {
  background-color: var(--semantic-color-surface-header);
  border-color: var(--accent);
}

.tab-item:focus-visible {
  outline: 2px solid var(--accent);
  outline-offset: 2px;
}

.tab-item.active {
  background-color: var(--semantic-color-info-bg);
  border-color: var(--accent);
  color: var(--primary);
  box-shadow: 0 1px 3px rgba(59, 130, 246, 0.1);
  font-weight: 600;
}

.flag-icon {
  font-size: 1.1rem;
}

.lang-code-badge {
  font-size: 0.7rem;
  background-color: rgba(0, 0, 0, 0.05);
  padding: 1px 4px;
  border-radius: 3px;
  color: var(--semantic-color-text-secondary);
}

.active-lang-banner {
  background-color: var(--semantic-color-surface-muted);
  border-left: 3px solid var(--accent);
  padding: 10px 12px;
  border-radius: 0 6px 6px 0;
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
}

.active-lang-banner p {
  margin: 0;
  font-size: 0.8rem;
  color: var(--semantic-color-text-secondary);
  line-height: 1.4;
  flex: 1;
}

.completion-metrics {
  display: flex;
}

.metric-badge {
  font-size: 0.75rem;
  background-color: var(--semantic-color-success-bg-soft);
  color: var(--semantic-color-success-dark);
  border: 1px solid var(--semantic-color-success-border-soft);
  padding: 2px 8px;
  border-radius: 9999px;
  font-weight: 600;
}
</style>
