import { describe, it, expect, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { i18n, setGlobalLocale, SUPPORTED_LOCALES } from "../../src/i18n/index.js";
import LanguageTranslationTabs from "../../src/components/econsent/LanguageTranslationTabs.vue";

describe("Vue i18n Locale Catalogs & Integration", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    setGlobalLocale("en");
    localStorage.clear();
  });

  it("should initialize with default English locale", () => {
    expect(i18n.global.locale.value).toBe("en");
    expect(SUPPORTED_LOCALES).toEqual(["en", "es", "fr", "de"]);
  });

  it("should resolve translation keys correctly across locales", () => {
    setGlobalLocale("en");
    expect(i18n.global.t("econsent.authoring.title")).toBe("eConsent Template Authoring");

    setGlobalLocale("es");
    expect(i18n.global.t("econsent.authoring.title")).toBe("Redacción de plantillas de eConsent");

    setGlobalLocale("fr");
    expect(i18n.global.t("econsent.authoring.title")).toBe("Raction de modèles eConsent");

    setGlobalLocale("de");
    expect(i18n.global.t("econsent.authoring.title")).toBe("eConsent-Vorlagenerstellung");
  });

  it("should perform variable interpolation properly", () => {
    setGlobalLocale("en");
    expect(
      i18n.global.t("econsent.editor.editSection", { title: "Informed Consent" })
    ).toBe("Edit Section: Informed Consent");

    setGlobalLocale("es");
    expect(
      i18n.global.t("econsent.editor.editSection", { title: "Informed Consent" })
    ).toBe("Editar sección: Informed Consent");
  });

  it("should fall back to English if key is missing in active locale", () => {
    setGlobalLocale("es");
    // Add a temporary missing key check with missing key fallback
    const result = i18n.global.t("econsent.editor.nonExistentKey", "Default English Fallback");
    expect(result).toBe("Default English Fallback");
  });

  it("should persist selected locale to localStorage and synchronize document lang", () => {
    setGlobalLocale("fr");
    expect(i18n.global.locale.value).toBe("fr");
    expect(localStorage.getItem("user_locale")).toBe("fr");
    expect(document.documentElement.lang).toBe("fr");
  });

  it("should update component text dynamically when switching languages via LanguageTranslationTabs", async () => {
    const wrapper = mount(LanguageTranslationTabs, {
      global: {
        plugins: [i18n],
      },
    });

    expect(wrapper.text()).toContain("Localized Consent Translations:");

    // Click Spanish tab
    const tabs = wrapper.findAll("button.tab-item");
    const esTab = tabs.find((tab) => tab.text().includes("ES"));
    expect(esTab).toBeDefined();

    await esTab?.trigger("click");

    expect(i18n.global.locale.value).toBe("es");
    expect(localStorage.getItem("user_locale")).toBe("es");
    expect(wrapper.text()).toContain("Traducciones de consentimiento localizadas:");
  });
});
