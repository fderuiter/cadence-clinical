/**
 * Pre-approved Regulatory Comprehension Check Quiz Templates
 * Organized across standard clinical regulatory domains.
 */

export const REGULATORY_DOMAINS = [
  "Voluntary Participation",
  "Study Purpose & Procedures",
  "Risks & Benefits",
  "HIPAA & Confidentiality",
  "Alternative Treatments & Rights",
];

export const QUIZ_TEMPLATE_CATALOG = [
  {
    id: "tmpl-vp-01",
    domain: "Voluntary Participation",
    text: "Can a participant withdraw from this clinical study at any time?",
    options: [
      "No, participation is legally binding once signed",
      "Yes, participation is voluntary and withdrawal can occur at any time without penalty",
      "Only if approved by the primary care physician",
      "Withdrawal is allowed only during the first 30 days",
    ],
    correctAnswerIndex: 1,
    hint: "Refer to Section 5: Voluntary Participation and Right to Withdraw.",
    translations: {
      en: {
        text: "Can a participant withdraw from this clinical study at any time?",
        options: [
          "No, participation is legally binding once signed",
          "Yes, participation is voluntary and withdrawal can occur at any time without penalty",
          "Only if approved by the primary care physician",
          "Withdrawal is allowed only during the first 30 days",
        ],
        hint: "Refer to Section 5: Voluntary Participation and Right to Withdraw.",
      },
    },
  },
  {
    id: "tmpl-vp-02",
    domain: "Voluntary Participation",
    text: "Will your medical care or legal rights be affected if you decline to participate in this study?",
    options: [
      "Yes, you will lose access to standard medical care at this facility",
      "No, declining to participate will not affect your regular medical care or legal rights",
      "You will be required to transfer to another medical center",
      "Your health insurance will be immediately cancelled",
    ],
    correctAnswerIndex: 1,
    hint: "Participation is completely voluntary and choosing not to participate carries no penalty or loss of benefits.",
    translations: {
      en: {
        text: "Will your medical care or legal rights be affected if you decline to participate in this study?",
        options: [
          "Yes, you will lose access to standard medical care at this facility",
          "No, declining to participate will not affect your regular medical care or legal rights",
          "You will be required to transfer to another medical center",
          "Your health insurance will be immediately cancelled",
        ],
        hint: "Participation is completely voluntary and choosing not to participate carries no penalty or loss of benefits.",
      },
    },
  },
  {
    id: "tmpl-sp-01",
    domain: "Study Purpose & Procedures",
    text: "What is the primary objective of participating in this clinical trial?",
    options: [
      "To receive guaranteed cure for pre-existing conditions",
      "To evaluate the safety, tolerability, and effectiveness of the investigational product",
      "To replace all standard health checkups with routine blood samples",
      "To participate in an unmonitored commercial health survey",
    ],
    correctAnswerIndex: 1,
    hint: "Review the Study Objective and Overview section of the consent document.",
    translations: {
      en: {
        text: "What is the primary objective of participating in this clinical trial?",
        options: [
          "To receive guaranteed cure for pre-existing conditions",
          "To evaluate the safety, tolerability, and effectiveness of the investigational product",
          "To replace all standard health checkups with routine blood samples",
          "To participate in an unmonitored commercial health survey",
        ],
        hint: "Review the Study Objective and Overview section of the consent document.",
      },
    },
  },
  {
    id: "tmpl-sp-02",
    domain: "Study Purpose & Procedures",
    text: "How often will study visit procedures and safety evaluations occur during participation?",
    options: [
      "Daily for 365 consecutive days without interruption",
      "According to the Schedule of Activities outlined in the consent document",
      "Whenever the participant decides to visit the clinic without notice",
      "Only once at the very conclusion of the clinical study",
    ],
    correctAnswerIndex: 1,
    hint: "Refer to the Visit Schedule and Study Procedures section.",
    translations: {
      en: {
        text: "How often will study visit procedures and safety evaluations occur during participation?",
        options: [
          "Daily for 365 consecutive days without interruption",
          "According to the Schedule of Activities outlined in the consent document",
          "Whenever the participant decides to visit the clinic without notice",
          "Only once at the very conclusion of the clinical study",
        ],
        hint: "Refer to the Visit Schedule and Study Procedures section.",
      },
    },
  },
  {
    id: "tmpl-rb-01",
    domain: "Risks & Benefits",
    text: "Does participating in this research study guarantee a direct personal health benefit?",
    options: [
      "Yes, guaranteed therapeutic cure is assured for all participants",
      "No, direct health benefits cannot be guaranteed, though results may advance future treatments",
      "Benefits are guaranteed only if all survey questionnaires are submitted",
      "Yes, all participants experience immediate complete symptom reversal",
    ],
    correctAnswerIndex: 1,
    hint: "Review the Potential Risks and Benefits section regarding direct medical benefits.",
    translations: {
      en: {
        text: "Does participating in this research study guarantee a direct personal health benefit?",
        options: [
          "Yes, guaranteed therapeutic cure is assured for all participants",
          "No, direct health benefits cannot be guaranteed, though results may advance future treatments",
          "Benefits are guaranteed only if all survey questionnaires are submitted",
          "Yes, all participants experience immediate complete symptom reversal",
        ],
        hint: "Review the Potential Risks and Benefits section regarding direct medical benefits.",
      },
    },
  },
  {
    id: "tmpl-rb-02",
    domain: "Risks & Benefits",
    text: "What should you do immediately if you experience an unexpected symptom or side effect during the study?",
    options: [
      "Wait until the next scheduled study visit in six months",
      "Contact the principal investigator or study coordinator immediately using the provided 24/7 hotline",
      "Stop taking all prescribed medications without informing the study team",
      "Search online forums for alternative remedies",
    ],
    correctAnswerIndex: 1,
    hint: "Refer to Safety Reporting and Emergency Contact instructions on the consent form.",
    translations: {
      en: {
        text: "What should you do immediately if you experience an unexpected symptom or side effect during the study?",
        options: [
          "Wait until the next scheduled study visit in six months",
          "Contact the principal investigator or study coordinator immediately using the provided 24/7 hotline",
          "Stop taking all prescribed medications without informing the study team",
          "Search online forums for alternative remedies",
        ],
        hint: "Refer to Safety Reporting and Emergency Contact instructions on the consent form.",
      },
    },
  },
  {
    id: "tmpl-hipaa-01",
    domain: "HIPAA & Confidentiality",
    text: "How are your personal medical records and identity protected during this clinical study?",
    options: [
      "Records are published publicly on open research websites",
      "Direct identifiers are removed and data is protected under HIPAA regulations and clinical privacy standards",
      "Records are shared with commercial marketing and advertising agencies",
      "No privacy protections are applied to clinical research records",
    ],
    correctAnswerIndex: 1,
    hint: "Review the Confidentiality and Data Protection section.",
    translations: {
      en: {
        text: "How are your personal medical records and identity protected during this clinical study?",
        options: [
          "Records are published publicly on open research websites",
          "Direct identifiers are removed and data is protected under HIPAA regulations and clinical privacy standards",
          "Records are shared with commercial marketing and advertising agencies",
          "No privacy protections are applied to clinical research records",
        ],
        hint: "Review the Confidentiality and Data Protection section.",
      },
    },
  },
  {
    id: "tmpl-hipaa-02",
    domain: "HIPAA & Confidentiality",
    text: "Who may inspect your coded research records for audit and compliance verification?",
    options: [
      "Any member of the general public upon written request",
      "Authorized regulatory bodies (e.g. FDA, IRB), study monitors, and ethics committees",
      "Commercial advertisers and insurance telemarketers",
      "No one is permitted to review study records under any circumstances",
    ],
    correctAnswerIndex: 1,
    hint: "Refer to the Record Inspection and Regulatory Oversight guidelines.",
    translations: {
      en: {
        text: "Who may inspect your coded research records for audit and compliance verification?",
        options: [
          "Any member of the general public upon written request",
          "Authorized regulatory bodies (e.g. FDA, IRB), study monitors, and ethics committees",
          "Commercial advertisers and insurance telemarketers",
          "No one is permitted to review study records under any circumstances",
        ],
        hint: "Refer to the Record Inspection and Regulatory Oversight guidelines.",
      },
    },
  },
  {
    id: "tmpl-alt-01",
    domain: "Alternative Treatments & Rights",
    text: "What alternative care options are available if you decide not to participate in this study?",
    options: [
      "There are no other medical care options available at this clinic",
      "Standard medical care, approved alternative medications, or non-participation without penalty",
      "You must agree to enter a different experimental research trial",
      "All medical treatment will be withheld until consent is signed",
    ],
    correctAnswerIndex: 1,
    hint: "Read the Alternative Treatments and Participant Rights section.",
    translations: {
      en: {
        text: "What alternative care options are available if you decide not to participate in this study?",
        options: [
          "There are no other medical care options available at this clinic",
          "Standard medical care, approved alternative medications, or non-participation without penalty",
          "You must agree to enter a different experimental research trial",
          "All medical treatment will be withheld until consent is signed",
        ],
        hint: "Read the Alternative Treatments and Participant Rights section.",
      },
    },
  },
  {
    id: "tmpl-alt-02",
    domain: "Alternative Treatments & Rights",
    text: "Whom should you contact regarding your rights as a research subject or in case of research-related injury?",
    options: [
      "The local law enforcement department",
      "The Institutional Review Board (IRB) or Ethics Committee listed on the consent form",
      "The pharmaceutical sponsor's sales representative",
      "The nearest hospital emergency room administrator",
    ],
    correctAnswerIndex: 1,
    hint: "Refer to Subject Rights and IRB Contact Information on the consent form.",
    translations: {
      en: {
        text: "Whom should you contact regarding your rights as a research subject or in case of research-related injury?",
        options: [
          "The local law enforcement department",
          "The Institutional Review Board (IRB) or Ethics Committee listed on the consent form",
          "The pharmaceutical sponsor's sales representative",
          "The nearest hospital emergency room administrator",
        ],
        hint: "Refer to Subject Rights and IRB Contact Information on the consent form.",
      },
    },
  },
];
