import { defineStore } from "pinia";
import { allowUnbackedAction } from "../utils/unbackedActions.js";

function translateText(text, targetLang) {
  if (!text) return "";
  if (targetLang === "es") {
    let t = text
      .replace(/Study Purpose/g, "Propósito del Estudio")
      .replace(/Risks & Benefits/g, "Riesgos y Beneficios")
      .replace(/Alternative Treatments/g, "Tratamientos Alternativos")
      .replace(/Confidentiality/g, "Confidencialidad")
      .replace(/Voluntary Participation/g, "Participación Voluntaria")
      .replace(
        /The purpose of this study is to evaluate/g,
        "El propósito de este estudio es evaluar"
      )
      .replace(/Potential risks include/g, "Los riesgos potenciales incluyen")
      .replace(
        /Alternative treatments include/g,
        "Los tratamientos alternativos incluyen"
      )
      .replace(/All clinical data/g, "Todos los datos clínicos")
      .replace(
        /Your participation is completely voluntary/g,
        "Su participación es completamente voluntaria"
      );
    if (t === text && !t.includes("(ES)")) {
      t = t + " (ES)";
    }
    return t;
  }
  if (targetLang === "fr") {
    let t = text
      .replace(/Study Purpose/g, "Objectif de l'étude")
      .replace(/Risks & Benefits/g, "Risques et avantages")
      .replace(/Alternative Treatments/g, "Traitements alternatifs")
      .replace(/Confidentiality/g, "Confidentialité")
      .replace(/Voluntary Participation/g, "Participation volontaire")
      .replace(
        /The purpose of this study is to evaluate/g,
        "Le but de cette étude est d'évaluer"
      )
      .replace(/Potential risks include/g, "Les risques potentiels comprennent")
      .replace(
        /Alternative treatments include/g,
        "Les traitements alternatifs comprennent"
      )
      .replace(/All clinical data/g, "Toutes les données cliniques")
      .replace(
        /Your participation is completely voluntary/g,
        "Votre participation est entièrement voluntary"
      );
    if (t === text && !t.includes("(FR)")) {
      t = t + " (FR)";
    }
    return t;
  }
  if (targetLang === "de") {
    let t = text
      .replace(/Study Purpose/g, "Zweck der Studie")
      .replace(/Risks & Benefits/g, "Risiken und Nutzen")
      .replace(/Alternative Treatments/g, "Alternative Behandlungen")
      .replace(/Confidentiality/g, "Vertraulichkeit")
      .replace(/Voluntary Participation/g, "Freiwillige Teilnahme")
      .replace(
        /The purpose of this study is to evaluate/g,
        "Der Zweck dieser Studie ist die Bewertung"
      )
      .replace(/Potential risks include/g, "Mögliche Risiken umfassen")
      .replace(
        /Alternative treatments include/g,
        "Alternative Behandlungen umfassen"
      )
      .replace(/All clinical data/g, "Alle klinischen Daten")
      .replace(
        /Your participation is completely voluntary/g,
        "Ihre Teilnahme ist völlig freiwillig"
      );
    if (t === text && !t.includes("(DE)")) {
      t = t + " (DE)";
    }
    return t;
  }
  return text + ` (${targetLang.toUpperCase()})`;
}

export const useEconsentStore = defineStore("econsent", {
  state: () => ({
    currentIcf: null,
    sections: [],
    quizQuestions: [],
    activeLanguage: "en",
    versionHistory: [],
    passingThreshold: 80,
    quizPassed: false,
    quizScore: 0,
    signedConsents: [],
  }),

  actions: {
    loadIcf(id) {
      // Mock loading an ICF linked to CADENCE-101 and active protocol version
      this.currentIcf = {
        id: id || "icf-001",
        title: "CADENCE-101 Informed Consent Form",
        version: "v1.0",
        protocolVersion: "v1.0",
        studyId: "CADENCE-101",
      };

      // Set default version history
      this.versionHistory = [
        {
          version: "v1.0",
          reason: "Initial template drafting",
          timestamp: new Date().toISOString(),
        },
      ];

      // Standard section templates
      const defaultSections = [
        {
          title: "Study Purpose",
          content:
            "<p>The purpose of this study is to evaluate the safety and efficacy of Cadence-001 in reducing blood pressure.</p>",
        },
        {
          title: "Risks & Benefits",
          content:
            "<p>Potential risks include mild headaches and dizziness. Benefits include potential reduction in hypertension.</p>",
        },
        {
          title: "Alternative Treatments",
          content:
            "<p>Alternative treatments include standard ACE inhibitors, lifestyle modifications, or other prescribed medications.</p>",
        },
        {
          title: "Confidentiality",
          content:
            "<p>All clinical data and subject records will be kept strictly confidential under HIPAA and clinical trial standards.</p>",
        },
        {
          title: "Voluntary Participation",
          content:
            "<p>Your participation is completely voluntary. You may withdraw from the clinical study at any time without penalty.</p>",
        },
      ];

      this.sections = defaultSections.map((sec, idx) => {
        const sid = `sec-${idx + 1}`;
        return {
          id: sid,
          title: sec.title,
          html: sec.content,
          translations: {
            en: { title: sec.title, html: sec.content, status: "Approved" },
            es: {
              title: sec.title + " (Spanish)",
              html: sec.content.replace("The purpose", "El propósito"),
              status: "Draft - Pending Review",
            },
            fr: {
              title: sec.title + " (French)",
              html: sec.content.replace("The purpose", "Le but"),
              status: "Draft - Pending Review",
            },
            de: {
              title: sec.title + " (German)",
              html: sec.content.replace("The purpose", "Der zweck"),
              status: "Draft - Pending Review",
            },
          },
        };
      });

      // Default comprehension check questions
      this.quizQuestions = [
        {
          id: "q-1",
          text: "What is the primary objective of this clinical study?",
          options: [
            "To test a new diet plan",
            "To evaluate the efficacy and safety of Cadence-001",
            "To perform surgery on hypertension patients",
            "To monitor weight loss",
          ],
          correctAnswerIndex: 1,
          hint: "Review the Study Purpose section.",
          translations: {
            en: {
              text: "What is the primary objective of this clinical study?",
              options: [
                "To test a new diet plan",
                "To evaluate the efficacy and safety of Cadence-001",
                "To perform surgery on hypertension patients",
                "To monitor weight loss",
              ],
              hint: "Review the Study Purpose section.",
            },
          },
        },
        {
          id: "q-2",
          text: "Is your participation in this study voluntary?",
          options: [
            "No, once I sign I cannot withdraw",
            "Yes, I can withdraw at any time without penalty",
            "Only if approved by the investigator",
            "No, it is mandatory",
          ],
          correctAnswerIndex: 1,
          hint: "Read the Voluntary Participation section.",
          translations: {
            en: {
              text: "Is your participation in this study voluntary?",
              options: [
                "No, once I sign I cannot withdraw",
                "Yes, I can withdraw at any time without penalty",
                "Only if approved by the investigator",
                "No, it is mandatory",
              ],
              hint: "Read the Voluntary Participation section.",
            },
          },
        },
      ];

      this.activeLanguage = "en";
      this.passingThreshold = 80;
      this.quizPassed = false;
      this.quizScore = 0;
    },

    addSection(title) {
      const sid = `sec-${Date.now()}`;
      const defaultHtml = `<p>Draft content for ${title}.</p>`;

      const newSection = {
        id: sid,
        title,
        html: defaultHtml,
        translations: {
          en: { title, html: defaultHtml },
          es: {
            title: title + " (ES)",
            html: `<p>Contenido borrador para ${title}.</p>`,
          },
          fr: {
            title: title + " (FR)",
            html: `<p>Ébauche de contenu pour ${title}.</p>`,
          },
          de: {
            title: title + " (DE)",
            html: `<p>Entwurfsinhalt für ${title}.</p>`,
          },
        },
      };

      this.sections.push(newSection);
      return newSection;
    },

    removeSection(sectionId) {
      const idx = this.sections.findIndex((s) => s.id === sectionId);
      if (idx !== -1) {
        this.sections.splice(idx, 1);
      }
    },

    reorderSections(fromIndex, toIndex) {
      if (
        fromIndex < 0 ||
        fromIndex >= this.sections.length ||
        toIndex < 0 ||
        toIndex >= this.sections.length
      ) {
        return;
      }
      const item = this.sections.splice(fromIndex, 1)[0];
      this.sections.splice(toIndex, 0, item);
    },

    updateSectionContent(sectionId, html) {
      const sec = this.sections.find((s) => s.id === sectionId);
      if (sec) {
        sec.html = html;
        // Also update translations for the current active language
        if (!sec.translations) {
          sec.translations = {};
        }
        if (!sec.translations[this.activeLanguage]) {
          sec.translations[this.activeLanguage] = { title: sec.title, html };
        } else {
          sec.translations[this.activeLanguage].html = html;
        }
      }
    },

    setLanguage(langCode) {
      this.activeLanguage = langCode;

      // Update active title and html for each section based on translations
      this.sections.forEach((sec) => {
        if (sec.translations && sec.translations[langCode]) {
          sec.title = sec.translations[langCode].title;
          sec.html = sec.translations[langCode].html;
        }
      });
    },

    evaluateQuiz(submittedAnswers = {}) {
      if (this.quizQuestions.length === 0) {
        this.quizScore = 100;
        this.quizPassed = true;
        return { score: 100, passed: true, correctCount: 0, total: 0 };
      }

      let correctCount = 0;
      this.quizQuestions.forEach((q) => {
        const submitted = submittedAnswers[q.id];
        if (
          submitted !== undefined &&
          parseInt(submitted, 10) === q.correctAnswerIndex
        ) {
          correctCount++;
        }
      });

      const total = this.quizQuestions.length;
      const score = Math.round((correctCount / total) * 100);
      const passed = score >= (this.passingThreshold || 80);

      this.quizScore = score;
      this.quizPassed = passed;

      return {
        score,
        passed,
        correctCount,
        total,
        passingThreshold: this.passingThreshold,
      };
    },

    resetQuiz() {
      this.quizPassed = false;
      this.quizScore = 0;
    },

    /** Returns false, recording nothing, when no service can record the signature (#4827). */
    recordSignedConsent(signatureRecord) {
      if (!allowUnbackedAction("Recording a consent signature")) return false;
      this.signedConsents.push({
        id: `sig-${Date.now()}`,
        timestamp: new Date().toISOString(),
        ...signatureRecord,
      });
      return true;
    },

    /** Returns false, publishing nothing, when no service can publish the ICF version (#4827). */
    publishIcfVersion(reason) {
      if (!this.currentIcf) return false;
      if (!allowUnbackedAction("Publishing an ICF version")) return false;
      const currentVerStr = this.currentIcf.version;

      // Increment version v1.0 -> v2.0
      let nextVerStr = "v2.0";
      const match = currentVerStr.match(/^v?(\d+)\.(\d+)$/);
      if (match) {
        const major = parseInt(match[1], 10);
        nextVerStr = `v${major + 1}.0`;
      }

      this.currentIcf.version = nextVerStr;
      this.currentIcf.protocolVersion = nextVerStr;

      const versionRecord = {
        version: nextVerStr,
        reason: reason || "Version incremented",
        timestamp: new Date().toISOString(),
      };

      this.versionHistory.push(versionRecord);
      return true;
    },

    autoTranslateDrafts({
      targetLocales = ["es", "fr", "de"],
      scope = "all",
      overwriteExisting = false,
    } = {}) {
      const locales = Array.isArray(targetLocales)
        ? targetLocales
        : [targetLocales];

      locales.forEach((lang) => {
        if (lang === "en") return;

        this.sections.forEach((sec) => {
          const sourceTitle = sec.translations?.en?.title || sec.title;
          const sourceHtml = sec.translations?.en?.html || sec.html;
          const existing = sec.translations?.[lang];

          const isUnedited =
            !existing ||
            !existing.html ||
            existing.status === "Draft - Pending Review";
          const isApproved = existing && existing.status === "Approved";
          const isEdited =
            existing &&
            existing.html &&
            existing.status !== "Draft - Pending Review" &&
            !isUnedited;

          if (scope === "unedited" && !isUnedited && !overwriteExisting) {
            return;
          }

          if (isApproved && !overwriteExisting) {
            return;
          }

          if (isEdited && !overwriteExisting) {
            return;
          }

          const translatedTitle = translateText(sourceTitle, lang);
          const translatedHtml = translateText(sourceHtml, lang);

          if (!sec.translations) sec.translations = {};
          sec.translations[lang] = {
            title: translatedTitle,
            html: translatedHtml,
            status: "Draft - Pending Review",
            updatedAt: new Date().toISOString(),
          };

          if (this.activeLanguage === lang) {
            sec.title = translatedTitle;
            sec.html = translatedHtml;
          }
        });
      });
    },

    exportXliff({ targetLocale = "es", version = "1.2" } = {}) {
      const icfId = this.currentIcf?.id || "icf-001";
      const escapeXml = (str) =>
        (str || "")
          .replace(/&/g, "&amp;")
          .replace(/</g, "&lt;")
          .replace(/>/g, "&gt;")
          .replace(/"/g, "&quot;")
          .replace(/'/g, "&apos;");

      if (version === "2.0") {
        const units = this.sections
          .flatMap((sec) => {
            const sourceTitle = sec.translations?.en?.title || sec.title || "";
            const targetTitle = sec.translations?.[targetLocale]?.title || "";
            const sourceHtml = sec.translations?.en?.html || sec.html || "";
            const targetHtml = sec.translations?.[targetLocale]?.html || "";
            return [
              `    <unit id="${sec.id}_title">\n      <segment>\n        <source>${escapeXml(sourceTitle)}</source>\n        <target>${escapeXml(targetTitle)}</target>\n      </segment>\n    </unit>`,
              `    <unit id="${sec.id}_html">\n      <segment>\n        <source>${escapeXml(sourceHtml)}</source>\n        <target>${escapeXml(targetHtml)}</target>\n      </segment>\n    </unit>`,
            ];
          })
          .join("\n");

        return `<?xml version="1.0" encoding="UTF-8"?>\n<xliff version="2.0" xmlns="urn:oasis:names:tc:xliff:document:2.0" srcLang="en" trgLang="${targetLocale}">\n  <file id="f1" original="${icfId}">\n${units}\n  </file>\n</xliff>`;
      } else {
        const transUnits = this.sections
          .flatMap((sec) => {
            const sourceTitle = sec.translations?.en?.title || sec.title || "";
            const targetTitle = sec.translations?.[targetLocale]?.title || "";
            const sourceHtml = sec.translations?.en?.html || sec.html || "";
            const targetHtml = sec.translations?.[targetLocale]?.html || "";
            return [
              `      <trans-unit id="${sec.id}_title">\n        <source>${escapeXml(sourceTitle)}</source>\n        <target>${escapeXml(targetTitle)}</target>\n      </trans-unit>`,
              `      <trans-unit id="${sec.id}_html">\n        <source>${escapeXml(sourceHtml)}</source>\n        <target>${escapeXml(targetHtml)}</target>\n      </trans-unit>`,
            ];
          })
          .join("\n");

        return `<?xml version="1.0" encoding="UTF-8"?>\n<xliff version="1.2" xmlns="urn:oasis:names:tc:xliff:document:1.2">\n  <file source-language="en" target-language="${targetLocale}" datatype="plaintext" original="${icfId}">\n    <header>\n      <tool tool-id="cadence-econsent" tool-name="Cadence eConsent Translator"/>\n    </header>\n    <body>\n${transUnits}\n    </body>\n  </file>\n</xliff>`;
      }
    },

    importXliff(xmlText, targetLocaleOverride = null) {
      if (!xmlText || typeof xmlText !== "string" || !xmlText.trim()) {
        return {
          success: false,
          error: "XLIFF file is empty or unreadable.",
        };
      }

      const parser = new DOMParser();
      const doc = parser.parseFromString(xmlText, "text/xml");

      const parserError = doc.querySelector("parsererror");
      if (parserError) {
        return {
          success: false,
          error: `XML Syntax Error: ${parserError.textContent.split("\n")[0] || "Invalid XML markup"}`,
        };
      }

      const root = doc.documentElement;
      if (!root || root.nodeName.toLowerCase() !== "xliff") {
        return {
          success: false,
          error: "XML Schema Error: Root element is not <xliff>.",
        };
      }

      let targetLocale = targetLocaleOverride;
      if (!targetLocale) {
        const fileEl = doc.querySelector("file");
        targetLocale =
          fileEl?.getAttribute("target-language") ||
          fileEl?.getAttribute("trgLang") ||
          root.getAttribute("trgLang") ||
          this.activeLanguage ||
          "es";
      }

      let updatedCount = 0;
      const mismatchedKeys = new Set();

      const transUnits = Array.from(doc.querySelectorAll("trans-unit, unit"));
      if (transUnits.length === 0) {
        return {
          success: false,
          error:
            "XML Schema Error: No translation units (<trans-unit> or <unit>) found in XLIFF file.",
        };
      }

      transUnits.forEach((unit) => {
        const id = unit.getAttribute("id");
        if (!id) return;

        let targetText = "";
        const targetEl = unit.querySelector("target");
        if (targetEl) {
          targetText = targetEl.textContent || targetEl.innerText || "";
        } else {
          const sourceEl = unit.querySelector("source");
          if (sourceEl) {
            targetText = sourceEl.textContent || sourceEl.innerText || "";
          }
        }

        let secId = id;
        let field = "html";

        const match = id.match(/^(.*?)[-_.:](title|html)$/i);
        if (match) {
          secId = match[1];
          field = match[2].toLowerCase();
        } else if (id.toLowerCase().includes("title")) {
          field = "title";
        }

        const section = this.sections.find((s) => s.id === secId);
        if (section) {
          if (!section.translations) section.translations = {};
          if (!section.translations[targetLocale]) {
            section.translations[targetLocale] = {
              title: section.title,
              html: section.html,
              status: "Vendor Verified",
            };
          }
          section.translations[targetLocale][field] = targetText;
          section.translations[targetLocale].status = "Vendor Verified";
          section.translations[targetLocale].updatedAt =
            new Date().toISOString();

          if (this.activeLanguage === targetLocale) {
            if (field === "title") section.title = targetText;
            if (field === "html") section.html = targetText;
          }
          updatedCount++;
        } else {
          mismatchedKeys.add(id);
        }
      });

      return {
        success: true,
        targetLocale,
        updatedCount,
        mismatchedKeys: Array.from(mismatchedKeys),
      };
    },
  },
});
