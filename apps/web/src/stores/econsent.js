import { defineStore } from 'pinia';

export const useEconsentStore = defineStore('econsent', {
  state: () => ({
    currentIcf: null,
    sections: [],
    quizQuestions: [],
    activeLanguage: 'en',
    versionHistory: [],
    passingThreshold: 80,
  }),

  actions: {
    loadIcf(id) {
      // Mock loading an ICF
      this.currentIcf = {
        id: id || 'icf-001',
        title: 'Informed Consent for Hypertension Study',
        version: 'v1.0',
        studyId: 'STUDY-USDM-001',
      };

      // Set default version history
      this.versionHistory = [
        {
          version: 'v1.0',
          reason: 'Initial template drafting',
          timestamp: new Date().toISOString(),
        }
      ];

      // Standard section templates
      const defaultSections = [
        { title: 'Study Purpose', content: '<p>The purpose of this study is to evaluate the safety and efficacy of Cadence-001 in reducing blood pressure.</p>' },
        { title: 'Risks & Benefits', content: '<p>Potential risks include mild headaches and dizziness. Benefits include potential reduction in hypertension.</p>' },
        { title: 'Alternative Treatments', content: '<p>Alternative treatments include standard ACE inhibitors, lifestyle modifications, or other prescribed medications.</p>' },
        { title: 'Confidentiality', content: '<p>All clinical data and subject records will be kept strictly confidential under HIPAA and clinical trial standards.</p>' },
        { title: 'Voluntary Participation', content: '<p>Your participation is completely voluntary. You may withdraw from the clinical study at any time without penalty.</p>' }
      ];

      this.sections = defaultSections.map((sec, idx) => {
        const sid = `sec-${idx + 1}`;
        return {
          id: sid,
          title: sec.title,
          html: sec.content,
          translations: {
            en: { title: sec.title, html: sec.content },
            es: { title: sec.title + ' (Spanish)', html: sec.content.replace('The purpose', 'El propósito') },
            fr: { title: sec.title + ' (French)', html: sec.content.replace('The purpose', 'Le but') },
            de: { title: sec.title + ' (German)', html: sec.content.replace('The purpose', 'Der zweck') }
          }
        };
      });

      // Default comprehension check questions
      this.quizQuestions = [
        {
          id: 'q-1',
          text: 'What is the primary objective of this clinical study?',
          options: [
            'To test a new diet plan',
            'To evaluate the efficacy and safety of Cadence-001',
            'To perform surgery on hypertension patients',
            'To monitor weight loss'
          ],
          correctAnswerIndex: 1,
          hint: 'Review the Study Purpose section.',
          translations: {
            en: {
              text: 'What is the primary objective of this clinical study?',
              options: [
                'To test a new diet plan',
                'To evaluate the efficacy and safety of Cadence-001',
                'To perform surgery on hypertension patients',
                'To monitor weight loss'
              ],
              hint: 'Review the Study Purpose section.'
            }
          }
        },
        {
          id: 'q-2',
          text: 'Is your participation in this study voluntary?',
          options: [
            'No, once I sign I cannot withdraw',
            'Yes, I can withdraw at any time without penalty',
            'Only if approved by the investigator',
            'No, it is mandatory'
          ],
          correctAnswerIndex: 1,
          hint: 'Read the Voluntary Participation section.',
          translations: {
            en: {
              text: 'Is your participation in this study voluntary?',
              options: [
                'No, once I sign I cannot withdraw',
                'Yes, I can withdraw at any time without penalty',
                'Only if approved by the investigator',
                'No, it is mandatory'
              ],
              hint: 'Read the Voluntary Participation section.'
            }
          }
        }
      ];

      this.activeLanguage = 'en';
      this.passingThreshold = 80;
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
          es: { title: title + ' (ES)', html: `<p>Contenido borrador para ${title}.</p>` },
          fr: { title: title + ' (FR)', html: `<p>Ébauche de contenu pour ${title}.</p>` },
          de: { title: title + ' (DE)', html: `<p>Entwurfsinhalt für ${title}.</p>` }
        }
      };

      this.sections.push(newSection);
      return newSection;
    },

    updateSectionContent(sectionId, html) {
      const sec = this.sections.find(s => s.id === sectionId);
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
      this.sections.forEach(sec => {
        if (sec.translations && sec.translations[langCode]) {
          sec.title = sec.translations[langCode].title;
          sec.html = sec.translations[langCode].html;
        }
      });
    },

    publishIcfVersion(reason) {
      if (!this.currentIcf) return;
      const currentVerStr = this.currentIcf.version;

      // Increment version v1.0 -> v2.0
      let nextVerStr = 'v2.0';
      const match = currentVerStr.match(/^v?(\d+)\.(\d+)$/);
      if (match) {
        const major = parseInt(match[1], 10);
        nextVerStr = `v${(major + 1)}.0`;
      }

      this.currentIcf.version = nextVerStr;

      const versionRecord = {
        version: nextVerStr,
        reason: reason || 'Version incremented',
        timestamp: new Date().toISOString(),
      };

      this.versionHistory.push(versionRecord);
    }
  }
});
