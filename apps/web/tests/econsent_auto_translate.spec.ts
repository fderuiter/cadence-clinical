import { describe, it, expect, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { useEconsentStore } from "../src/stores/econsent.js";
import LanguageTranslationTabs from "../src/components/econsent/LanguageTranslationTabs.vue";
import AutoTranslationDrawer from "../src/components/econsent/AutoTranslationDrawer.vue";

describe("eConsent Auto-Translation & Vendor XLIFF Import", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
  });

  it("opens AutoTranslationDrawer when 'Auto-Translate' button is clicked in LanguageTranslationTabs", async () => {
    const store = useEconsentStore();
    store.loadIcf();

    const wrapper = mount(LanguageTranslationTabs, {
      global: {
        mocks: {
          $t: (key, params) => {
            if (key === "econsent.translationTabs.headerLabel")
              return "Localized Consent Translations:";
            if (key === "econsent.translationTabs.bannerText")
              return `Editing in ${params?.language}`;
            if (key === "econsent.translationTabs.sectionsMetric")
              return `Sections: ${params?.translated} / ${params?.total}`;
            return key;
          },
        },
      },
    });

    // AutoTranslationDrawer should initially not be visible or open
    expect(wrapper.findComponent(AutoTranslationDrawer).props("isOpen")).toBe(
      false
    );

    // Click Auto-Translate button
    const autoTranslateBtn = wrapper.find(".btn-auto-translate");
    expect(autoTranslateBtn.exists()).toBe(true);
    await autoTranslateBtn.trigger("click");

    // Drawer should now be open
    expect(wrapper.findComponent(AutoTranslationDrawer).props("isOpen")).toBe(
      true
    );
  });

  it("generates automated translation drafts for selected target locales and marks them with 'Draft - Pending Review'", async () => {
    const store = useEconsentStore();
    store.loadIcf();

    // Call store action
    store.autoTranslateDrafts({
      targetLocales: ["es", "fr"],
      scope: "all",
      overwriteExisting: true,
    });

    // Check Spanish translations
    store.sections.forEach((sec) => {
      expect(sec.translations.es).toBeDefined();
      expect(sec.translations.es.status).toBe("Draft - Pending Review");
      expect(sec.translations.es.title).toBeTruthy();
      expect(sec.translations.es.html).toBeTruthy();

      expect(sec.translations.fr).toBeDefined();
      expect(sec.translations.fr.status).toBe("Draft - Pending Review");
      expect(sec.translations.fr.title).toBeTruthy();
      expect(sec.translations.fr.html).toBeTruthy();
    });
  });

  it("protects approved/edited translations from overwrite unless explicitly requested", async () => {
    const store = useEconsentStore();
    store.loadIcf();

    // Set a human approved translation in Spanish
    store.sections[0].translations.es = {
      title: "Human Validated Title",
      html: "<p>Human validated HTML</p>",
      status: "Approved",
    };

    // Run auto-translate without overwrite
    store.autoTranslateDrafts({
      targetLocales: ["es"],
      scope: "unedited",
      overwriteExisting: false,
    });

    // The approved section should remain unchanged
    expect(store.sections[0].translations.es.title).toBe(
      "Human Validated Title"
    );
    expect(store.sections[0].translations.es.status).toBe("Approved");

    // Run auto-translate WITH overwrite
    store.autoTranslateDrafts({
      targetLocales: ["es"],
      scope: "all",
      overwriteExisting: true,
    });

    // Now it should be overwritten and set to Draft - Pending Review
    expect(store.sections[0].translations.es.status).toBe(
      "Draft - Pending Review"
    );
    expect(store.sections[0].translations.es.title).not.toBe(
      "Human Validated Title"
    );
  });

  it("exports valid XLIFF 1.2 and 2.0 XML bundles containing source sections and metadata", () => {
    const store = useEconsentStore();
    store.loadIcf();

    // Export XLIFF 1.2
    const xliff12 = store.exportXliff({ targetLocale: "es", version: "1.2" });
    expect(xliff12).toContain('<?xml version="1.0" encoding="UTF-8"?>');
    expect(xliff12).toContain('<xliff version="1.2"');
    expect(xliff12).toContain('target-language="es"');
    expect(xliff12).toContain('<trans-unit id="sec-1_title"');
    expect(xliff12).toContain('<trans-unit id="sec-1_html"');

    // Export XLIFF 2.0
    const xliff20 = store.exportXliff({ targetLocale: "fr", version: "2.0" });
    expect(xliff20).toContain('<xliff version="2.0"');
    expect(xliff20).toContain('trgLang="fr"');
    expect(xliff20).toContain('<unit id="sec-1_title"');
    expect(xliff20).toContain('<unit id="sec-1_html"');
  });

  it("imports valid XLIFF XML files, updates store state, and sets status to 'Vendor Verified'", () => {
    const store = useEconsentStore();
    store.loadIcf();

    const xliffContent = `<?xml version="1.0" encoding="UTF-8"?>
<xliff version="1.2" xmlns="urn:oasis:names:tc:xliff:document:1.2">
  <file source-language="en" target-language="es" datatype="plaintext" original="icf-001">
    <body>
      <trans-unit id="sec-1_title">
        <source>Study Purpose</source>
        <target>Propósito Oficial del Estudio Clínico</target>
      </trans-unit>
      <trans-unit id="sec-1_html">
        <source>Content</source>
        <target><![CDATA[<p>El propósito de este estudio es evaluar la eficacia.</p>]]></target>
      </trans-unit>
    </body>
  </file>
</xliff>`;

    const result = store.importXliff(xliffContent);
    expect(result.success).toBe(true);
    expect(result.targetLocale).toBe("es");
    expect(result.updatedCount).toBe(2);
    expect(result.mismatchedKeys.length).toBe(0);

    // Verify section updated in store
    expect(store.sections[0].translations.es.title).toBe(
      "Propósito Oficial del Estudio Clínico"
    );
    expect(store.sections[0].translations.es.html).toContain(
      "El propósito de este estudio es evaluar la eficacia."
    );
    expect(store.sections[0].translations.es.status).toBe("Vendor Verified");
  });

  it("rejects invalid XML syntax or non-XLIFF root elements with descriptive error messages", () => {
    const store = useEconsentStore();
    store.loadIcf();

    // Malformed XML
    const invalidXml = `<?xml version="1.0"?><xliff><file><body<trans-unit></xliff>`;
    const syntaxRes = store.importXliff(invalidXml);
    expect(syntaxRes.success).toBe(false);
    expect(syntaxRes.error).toContain("XML Syntax Error");

    // Non-XLIFF root
    const nonXliff = `<?xml version="1.0"?><document><item>test</item></document>`;
    const schemaRes = store.importXliff(nonXliff);
    expect(schemaRes.success).toBe(false);
    expect(schemaRes.error).toBe(
      "XML Schema Error: Root element is not <xliff>."
    );
  });

  it("allows partial import and reports warnings when XLIFF contains mismatched section keys", () => {
    const store = useEconsentStore();
    store.loadIcf();

    const xliffMismatched = `<?xml version="1.0" encoding="UTF-8"?>
<xliff version="1.2" xmlns="urn:oasis:names:tc:xliff:document:1.2">
  <file source-language="en" target-language="es" datatype="plaintext" original="icf-001">
    <body>
      <trans-unit id="sec-1_title">
        <source>Study Purpose</source>
        <target>Propósito Válido</target>
      </trans-unit>
      <trans-unit id="sec-unknown-999_title">
        <source>Unknown</source>
        <target>Desconocido</target>
      </trans-unit>
    </body>
  </file>
</xliff>`;

    const result = store.importXliff(xliffMismatched);
    expect(result.success).toBe(true);
    expect(result.updatedCount).toBe(1);
    expect(result.mismatchedKeys).toContain("sec-unknown-999_title");

    // Valid section updated
    expect(store.sections[0].translations.es.title).toBe("Propósito Válido");
  });
});
