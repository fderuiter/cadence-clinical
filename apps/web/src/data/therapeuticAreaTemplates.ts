import type { ClinicalIconName } from "ui";

export interface StudyArm {
  id?: string;
  name: string;
  arm_type: "EXPERIMENTAL" | "ACTIVE_COMPARATOR" | "PLACEBO";
  description: string;
  target_sample_size: number;
}

export interface StudyEpoch {
  id?: string;
  name: string;
  epoch_type: "SCREENING" | "TREATMENT" | "WASHOUT" | "FOLLOW_UP";
  sequence_index: number;
  duration_weeks?: number;
}

export interface PlannedVisit {
  id?: string;
  visit_name: string;
  epoch_name: string;
  target_day: number;
  window_lower_days: number;
  window_upper_days: number;
  is_mandatory: boolean;
}

export interface SoAActivity {
  id?: string;
  activity_name: string;
  cdash_domain: string;
  biomedical_concept_code: string;
  assigned_visit_names: string[];
}

export interface IECriterion {
  id?: string;
  criterion_type: "INCLUSION" | "EXCLUSION";
  identifier: string;
  text_expression: string;
  logical_expression: string;
}

export interface TherapeuticAreaTemplate {
  id: string;
  title: string;
  category: string;
  description: string;
  icon: ClinicalIconName;
  defaultPhase: string;
  defaultRandomizationRatio: string;
  protocol_id: string;
  study_title: string;
  confidence_score: number;
  arms: StudyArm[];
  epochs: StudyEpoch[];
  visits: PlannedVisit[];
  activities: SoAActivity[];
  criteria: IECriterion[];
}

export const THERAPEUTIC_AREA_TEMPLATES: Record<
  string,
  TherapeuticAreaTemplate
> = {
  oncology: {
    id: "oncology",
    title: "Oncology / Solid Tumors (NSCLC)",
    category: "Oncology",
    description:
      "Phase II/III double-blind oncology template with RECIST 1.1 tumor evaluations, safety labs, and crossover washout.",
    icon: "flask-conical",
    defaultPhase: "PHASE_II",
    defaultRandomizationRatio: "1:1",
    protocol_id: "CDNC-2026-ONC",
    study_title:
      "A Phase II Randomized Double-Blind Study of Investigational Kinase Inhibitor in Advanced Non-Small Cell Lung Cancer",
    confidence_score: 0.98,
    arms: [
      {
        id: "arm_1",
        name: "Investigational Arm (Compound X 100mg Daily)",
        arm_type: "EXPERIMENTAL",
        description:
          "Active targeted small molecule inhibitor administered daily in 28-day cycles",
        target_sample_size: 150,
      },
      {
        id: "arm_2",
        name: "Standard of Care Comparator (Pemetrexed/Cisplatin)",
        arm_type: "ACTIVE_COMPARATOR",
        description: "Standard doublet chemotherapy regimen every 21 days",
        target_sample_size: 150,
      },
    ],
    epochs: [
      {
        id: "ep_1",
        name: "Screening Epoch",
        epoch_type: "SCREENING",
        sequence_index: 1,
        duration_weeks: 2,
      },
      {
        id: "ep_2",
        name: "Treatment Epoch",
        epoch_type: "TREATMENT",
        sequence_index: 2,
        duration_weeks: 24,
      },
      {
        id: "ep_3",
        name: "Washout Epoch",
        epoch_type: "WASHOUT",
        sequence_index: 3,
        duration_weeks: 2,
      },
      {
        id: "ep_4",
        name: "Safety Follow-up",
        epoch_type: "FOLLOW_UP",
        sequence_index: 4,
        duration_weeks: 12,
      },
    ],
    visits: [
      {
        id: "vis_1",
        visit_name: "Screening (Day -14 to -1)",
        epoch_name: "Screening Epoch",
        target_day: -7,
        window_lower_days: 7,
        window_upper_days: 0,
        is_mandatory: true,
      },
      {
        id: "vis_2",
        visit_name: "Visit 1 / Baseline (Day 1)",
        epoch_name: "Treatment Epoch",
        target_day: 1,
        window_lower_days: 0,
        window_upper_days: 1,
        is_mandatory: true,
      },
      {
        id: "vis_3",
        visit_name: "Visit 2 / Week 2 (Day 14)",
        epoch_name: "Treatment Epoch",
        target_day: 14,
        window_lower_days: 2,
        window_upper_days: 2,
        is_mandatory: true,
      },
      {
        id: "vis_4",
        visit_name: "Visit 3 / Week 4 (Day 28)",
        epoch_name: "Treatment Epoch",
        target_day: 28,
        window_lower_days: 3,
        window_upper_days: 3,
        is_mandatory: true,
      },
      {
        id: "vis_5",
        visit_name: "Follow-up (Day 60)",
        epoch_name: "Safety Follow-up",
        target_day: 60,
        window_lower_days: 5,
        window_upper_days: 5,
        is_mandatory: true,
      },
    ],
    activities: [
      {
        id: "act_1",
        activity_name: "Informed Consent Form",
        cdash_domain: "DM",
        biomedical_concept_code: "C16468",
        assigned_visit_names: ["Screening (Day -14 to -1)"],
      },
      {
        id: "act_2",
        activity_name: "Vital Signs Assessment",
        cdash_domain: "VS",
        biomedical_concept_code: "C154628",
        assigned_visit_names: [
          "Screening (Day -14 to -1)",
          "Visit 1 / Baseline (Day 1)",
          "Visit 2 / Week 2 (Day 14)",
          "Visit 3 / Week 4 (Day 28)",
          "Follow-up (Day 60)",
        ],
      },
      {
        id: "act_3",
        activity_name: "12-Lead Electrocardiogram",
        cdash_domain: "EG",
        biomedical_concept_code: "C38053",
        assigned_visit_names: [
          "Screening (Day -14 to -1)",
          "Visit 1 / Baseline (Day 1)",
          "Visit 3 / Week 4 (Day 28)",
        ],
      },
      {
        id: "act_4",
        activity_name: "Safety Laboratory Panel",
        cdash_domain: "LB",
        biomedical_concept_code: "C25294",
        assigned_visit_names: [
          "Screening (Day -14 to -1)",
          "Visit 1 / Baseline (Day 1)",
          "Visit 2 / Week 2 (Day 14)",
          "Visit 3 / Week 4 (Day 28)",
          "Follow-up (Day 60)",
        ],
      },
      {
        id: "act_5",
        activity_name: "Visual Analog Scale (VAS) Pain Score",
        cdash_domain: "QS",
        biomedical_concept_code: "C21120",
        assigned_visit_names: [
          "Visit 1 / Baseline (Day 1)",
          "Visit 2 / Week 2 (Day 14)",
          "Visit 3 / Week 4 (Day 28)",
        ],
      },
      {
        id: "act_6",
        activity_name: "Physical Exam & Body Map",
        cdash_domain: "PE",
        biomedical_concept_code: "C20989",
        assigned_visit_names: [
          "Screening (Day -14 to -1)",
          "Visit 1 / Baseline (Day 1)",
          "Follow-up (Day 60)",
        ],
      },
    ],
    criteria: [
      {
        id: "crit_1",
        criterion_type: "INCLUSION",
        identifier: "INC-01",
        text_expression: "Age >= 18 years at the time of screening.",
        logical_expression: "DM.AGE >= 18",
      },
      {
        id: "crit_2",
        criterion_type: "INCLUSION",
        identifier: "INC-02",
        text_expression:
          "Histologically documented advanced or metastatic Non-Small Cell Lung Cancer.",
        logical_expression: "TU.DIAGNOSIS == 'NSCLC_ADVANCED'",
      },
      {
        id: "crit_3",
        criterion_type: "EXCLUSION",
        identifier: "EXC-01",
        text_expression:
          "Severe uncontrolled hypertension (SBP > 160 or DBP > 100).",
        logical_expression: "VS.SYSBP > 160 || VS.DIABP > 100",
      },
      {
        id: "crit_4",
        criterion_type: "EXCLUSION",
        identifier: "EXC-02",
        text_expression: "Baseline QTc prolongation > 480 ms.",
        logical_expression: "EG.EGQTC > 480",
      },
    ],
  },

  cardiology: {
    id: "cardiology",
    title: "Cardiology / Heart Failure",
    category: "Cardiology",
    description:
      "Cardiovascular trial template targeting NYHA Class II-IV Heart Failure with NT-proBNP biomarker and 6-minute walk monitoring.",
    icon: "stethoscope",
    defaultPhase: "PHASE_III",
    defaultRandomizationRatio: "1:1",
    protocol_id: "CDNC-2026-CARD",
    study_title:
      "A Phase III Randomized Double-Blind Study of Investigational Inotrope in Patients with Heart Failure with Reduced Ejection Fraction",
    confidence_score: 0.99,
    arms: [
      {
        id: "arm_card_1",
        name: "Investigational Inotrope (50mg BID)",
        arm_type: "EXPERIMENTAL",
        description:
          "Novel cardiac myosin activator administered orally twice daily",
        target_sample_size: 200,
      },
      {
        id: "arm_card_2",
        name: "Placebo Control + Guideline Directed Medical Therapy",
        arm_type: "PLACEBO",
        description:
          "Matching placebo combined with standard heart failure background therapy",
        target_sample_size: 200,
      },
    ],
    epochs: [
      {
        id: "ep_card_1",
        name: "Screening Epoch",
        epoch_type: "SCREENING",
        sequence_index: 1,
        duration_weeks: 2,
      },
      {
        id: "ep_card_2",
        name: "Dose Titration & Treatment",
        epoch_type: "TREATMENT",
        sequence_index: 2,
        duration_weeks: 16,
      },
      {
        id: "ep_card_3",
        name: "Washout Epoch",
        epoch_type: "WASHOUT",
        sequence_index: 3,
        duration_weeks: 2,
      },
      {
        id: "ep_card_4",
        name: "Long-Term Follow-up",
        epoch_type: "FOLLOW_UP",
        sequence_index: 4,
        duration_weeks: 12,
      },
    ],
    visits: [
      {
        id: "vis_card_1",
        visit_name: "Screening (Day -14 to -1)",
        epoch_name: "Screening Epoch",
        target_day: -7,
        window_lower_days: 7,
        window_upper_days: 0,
        is_mandatory: true,
      },
      {
        id: "vis_card_2",
        visit_name: "Visit 1 / Randomization (Day 1)",
        epoch_name: "Dose Titration & Treatment",
        target_day: 1,
        window_lower_days: 0,
        window_upper_days: 1,
        is_mandatory: true,
      },
      {
        id: "vis_card_3",
        visit_name: "Visit 2 / Week 4 Titration (Day 28)",
        epoch_name: "Dose Titration & Treatment",
        target_day: 28,
        window_lower_days: 3,
        window_upper_days: 3,
        is_mandatory: true,
      },
      {
        id: "vis_card_4",
        visit_name: "Visit 3 / Week 12 Primary Endpoint (Day 84)",
        epoch_name: "Dose Titration & Treatment",
        target_day: 84,
        window_lower_days: 5,
        window_upper_days: 5,
        is_mandatory: true,
      },
      {
        id: "vis_card_5",
        visit_name: "Safety Follow-up (Day 120)",
        epoch_name: "Long-Term Follow-up",
        target_day: 120,
        window_lower_days: 7,
        window_upper_days: 7,
        is_mandatory: true,
      },
    ],
    activities: [
      {
        id: "act_card_1",
        activity_name: "Informed Consent Form",
        cdash_domain: "DM",
        biomedical_concept_code: "C16468",
        assigned_visit_names: ["Screening (Day -14 to -1)"],
      },
      {
        id: "act_card_2",
        activity_name: "Vital Signs & Blood Pressure",
        cdash_domain: "VS",
        biomedical_concept_code: "C154628",
        assigned_visit_names: [
          "Screening (Day -14 to -1)",
          "Visit 1 / Randomization (Day 1)",
          "Visit 2 / Week 4 Titration (Day 28)",
          "Visit 3 / Week 12 Primary Endpoint (Day 84)",
          "Safety Follow-up (Day 120)",
        ],
      },
      {
        id: "act_card_3",
        activity_name: "12-Lead ECG & Holter Monitoring",
        cdash_domain: "EG",
        biomedical_concept_code: "C38053",
        assigned_visit_names: [
          "Screening (Day -14 to -1)",
          "Visit 1 / Randomization (Day 1)",
          "Visit 3 / Week 12 Primary Endpoint (Day 84)",
        ],
      },
      {
        id: "act_card_4",
        activity_name: "NT-proBNP Biomarker & Safety Labs",
        cdash_domain: "LB",
        biomedical_concept_code: "C25294",
        assigned_visit_names: [
          "Screening (Day -14 to -1)",
          "Visit 1 / Randomization (Day 1)",
          "Visit 2 / Week 4 Titration (Day 28)",
          "Visit 3 / Week 12 Primary Endpoint (Day 84)",
          "Safety Follow-up (Day 120)",
        ],
      },
      {
        id: "act_card_5",
        activity_name: "KCCQ-12 Heart Failure Questionnaire",
        cdash_domain: "QS",
        biomedical_concept_code: "C17048",
        assigned_visit_names: [
          "Visit 1 / Randomization (Day 1)",
          "Visit 3 / Week 12 Primary Endpoint (Day 84)",
        ],
      },
      {
        id: "act_card_6",
        activity_name: "Echocardiography & Physical Exam",
        cdash_domain: "PE",
        biomedical_concept_code: "C20989",
        assigned_visit_names: [
          "Screening (Day -14 to -1)",
          "Visit 3 / Week 12 Primary Endpoint (Day 84)",
        ],
      },
    ],
    criteria: [
      {
        id: "crit_card_1",
        criterion_type: "INCLUSION",
        identifier: "INC-01",
        text_expression:
          "Documented NYHA Class II-IV Heart Failure with LVEF <= 40%.",
        logical_expression: "PE.LVEF <= 40 && QS.NYHA_CLASS >= 2",
      },
      {
        id: "crit_card_2",
        criterion_type: "INCLUSION",
        identifier: "INC-02",
        text_expression: "Elevated plasma NT-proBNP >= 600 pg/mL at screening.",
        logical_expression: "LB.NTPROBNP >= 600",
      },
      {
        id: "crit_card_3",
        criterion_type: "EXCLUSION",
        identifier: "EXC-01",
        text_expression:
          "Acute myocardial infarction or coronary revascularization within 30 days.",
        logical_expression: "MH.AMI_30DAYS == TRUE",
      },
    ],
  },

  neurology: {
    id: "neurology",
    title: "Neurology / Alzheimer's & MS",
    category: "Neurology",
    description:
      "Neurological disease protocol featuring volumetric brain MRI, CSF amyloid/tau biomarkers, and ADAS-Cog scale assessments.",
    icon: "sparkles",
    defaultPhase: "PHASE_II",
    defaultRandomizationRatio: "1:1",
    protocol_id: "CDNC-2026-NEUR",
    study_title:
      "A Phase II Randomized Double-Blind Study of Anti-Amyloid Monoclonal Antibody in Mild Cognitive Impairment Due to Alzheimer's Disease",
    confidence_score: 0.97,
    arms: [
      {
        id: "arm_neur_1",
        name: "Investigational Antibody (10mg/kg Monthly IV)",
        arm_type: "EXPERIMENTAL",
        description:
          "Intravenous monoclonal antibody targeting misfolded amyloid oligomers",
        target_sample_size: 120,
      },
      {
        id: "arm_neur_2",
        name: "Placebo Infusion Control",
        arm_type: "PLACEBO",
        description: "Matching IV saline placebo infusion",
        target_sample_size: 120,
      },
    ],
    epochs: [
      {
        id: "ep_neur_1",
        name: "Screening & Biomarker Epoch",
        epoch_type: "SCREENING",
        sequence_index: 1,
        duration_weeks: 4,
      },
      {
        id: "ep_neur_2",
        name: "Infusion & Assessment Phase",
        epoch_type: "TREATMENT",
        sequence_index: 2,
        duration_weeks: 24,
      },
      {
        id: "ep_neur_3",
        name: "Washout Epoch",
        epoch_type: "WASHOUT",
        sequence_index: 3,
        duration_weeks: 4,
      },
      {
        id: "ep_neur_4",
        name: "Extended Safety Follow-up",
        epoch_type: "FOLLOW_UP",
        sequence_index: 4,
        duration_weeks: 24,
      },
    ],
    visits: [
      {
        id: "vis_neur_1",
        visit_name: "Screening (Day -28 to -1)",
        epoch_name: "Screening & Biomarker Epoch",
        target_day: -14,
        window_lower_days: 14,
        window_upper_days: 0,
        is_mandatory: true,
      },
      {
        id: "vis_neur_2",
        visit_name: "Visit 1 / First Infusion (Day 1)",
        epoch_name: "Infusion & Assessment Phase",
        target_day: 1,
        window_lower_days: 0,
        window_upper_days: 1,
        is_mandatory: true,
      },
      {
        id: "vis_neur_3",
        visit_name: "Visit 2 / Week 4 Infusion (Day 28)",
        epoch_name: "Infusion & Assessment Phase",
        target_day: 28,
        window_lower_days: 3,
        window_upper_days: 3,
        is_mandatory: true,
      },
      {
        id: "vis_neur_4",
        visit_name: "Visit 3 / Week 12 Evaluation (Day 84)",
        epoch_name: "Infusion & Assessment Phase",
        target_day: 84,
        window_lower_days: 5,
        window_upper_days: 5,
        is_mandatory: true,
      },
      {
        id: "vis_neur_5",
        visit_name: "Extended Follow-up (Day 180)",
        epoch_name: "Extended Safety Follow-up",
        target_day: 180,
        window_lower_days: 7,
        window_upper_days: 7,
        is_mandatory: true,
      },
    ],
    activities: [
      {
        id: "act_neur_1",
        activity_name: "Informed Consent & Caregiver Consent",
        cdash_domain: "DM",
        biomedical_concept_code: "C16468",
        assigned_visit_names: ["Screening (Day -28 to -1)"],
      },
      {
        id: "act_neur_2",
        activity_name: "Vital Signs & Neurological Exam",
        cdash_domain: "VS",
        biomedical_concept_code: "C154628",
        assigned_visit_names: [
          "Screening (Day -28 to -1)",
          "Visit 1 / First Infusion (Day 1)",
          "Visit 2 / Week 4 Infusion (Day 28)",
          "Visit 3 / Week 12 Evaluation (Day 84)",
          "Extended Follow-up (Day 180)",
        ],
      },
      {
        id: "act_neur_3",
        activity_name: "Volumetric Brain MRI & ARIA Safety Scan",
        cdash_domain: "PE",
        biomedical_concept_code: "C20989",
        assigned_visit_names: [
          "Screening (Day -28 to -1)",
          "Visit 3 / Week 12 Evaluation (Day 84)",
          "Extended Follow-up (Day 180)",
        ],
      },
      {
        id: "act_neur_4",
        activity_name: "CSF & Plasma Amyloid Beta/Tau Panel",
        cdash_domain: "LB",
        biomedical_concept_code: "C25294",
        assigned_visit_names: [
          "Screening (Day -28 to -1)",
          "Visit 3 / Week 12 Evaluation (Day 84)",
        ],
      },
      {
        id: "act_neur_5",
        activity_name: "ADAS-Cog13 & MMSE Cognitive Battery",
        cdash_domain: "QS",
        biomedical_concept_code: "C17048",
        assigned_visit_names: [
          "Screening (Day -28 to -1)",
          "Visit 1 / First Infusion (Day 1)",
          "Visit 3 / Week 12 Evaluation (Day 84)",
        ],
      },
      {
        id: "act_neur_6",
        activity_name: "Safety ECG Monitoring",
        cdash_domain: "EG",
        biomedical_concept_code: "C38053",
        assigned_visit_names: [
          "Screening (Day -28 to -1)",
          "Visit 1 / First Infusion (Day 1)",
          "Extended Follow-up (Day 180)",
        ],
      },
    ],
    criteria: [
      {
        id: "crit_neur_1",
        criterion_type: "INCLUSION",
        identifier: "INC-01",
        text_expression: "Age 55 to 85 years at time of screening.", // deid-ignore
        logical_expression: "DM.AGE >= 55 && DM.AGE <= 85",
      },
      {
        id: "crit_neur_2",
        criterion_type: "INCLUSION",
        identifier: "INC-02",
        text_expression:
          "Confirmed amyloid pathology by CSF A-beta 42/40 ratio or Amyloid PET scan.",
        logical_expression:
          "LB.CSF_AMYLOID_RATIO < 0.1 || IS.PET_AMYLOID == 'POSITIVE'",
      },
      {
        id: "crit_neur_3",
        criterion_type: "EXCLUSION",
        identifier: "EXC-01",
        text_expression:
          "Brain MRI evidence of > 4 microhemorrhages or superficial siderosis.",
        logical_expression: "IS.MRI_MICROHEM_COUNT > 4",
      },
    ],
  },

  immunology: {
    id: "immunology",
    title: "Immunology / Rheumatoid Arthritis",
    category: "Immunology",
    description:
      "3-arm immunology trial template with DAS28 joint counts, inflammatory CRP/ESR markers, and HAQ-DI physical function disability index.",
    icon: "shield-check",
    defaultPhase: "PHASE_III",
    defaultRandomizationRatio: "1:1:1",
    protocol_id: "CDNC-2026-IMM",
    study_title:
      "A Phase III Randomized Active- and Placebo-Controlled Study of Investigational JAK Inhibitor in Patients with Active Rheumatoid Arthritis",
    confidence_score: 0.98,
    arms: [
      {
        id: "arm_imm_1",
        name: "Investigational JAK Inhibitor (5mg Oral QD)",
        arm_type: "EXPERIMENTAL",
        description: "Targeted synthetic DMARD administered orally once daily",
        target_sample_size: 180,
      },
      {
        id: "arm_imm_2",
        name: "Methotrexate Comparator (15-25mg Weekly)",
        arm_type: "ACTIVE_COMPARATOR",
        description: "Standard anchor conventional synthetic DMARD",
        target_sample_size: 180,
      },
      {
        id: "arm_imm_3",
        name: "Placebo Control Arm",
        arm_type: "PLACEBO",
        description:
          "Matching oral placebo tablet QD with rescue option at Week 12",
        target_sample_size: 90,
      },
    ],
    epochs: [
      {
        id: "ep_imm_1",
        name: "Screening Phase",
        epoch_type: "SCREENING",
        sequence_index: 1,
        duration_weeks: 3,
      },
      {
        id: "ep_imm_2",
        name: "Main Treatment Epoch",
        epoch_type: "TREATMENT",
        sequence_index: 2,
        duration_weeks: 12,
      },
      {
        id: "ep_imm_3",
        name: "Washout Epoch",
        epoch_type: "WASHOUT",
        sequence_index: 3,
        duration_weeks: 2,
      },
      {
        id: "ep_imm_4",
        name: "Safety Monitoring Follow-up",
        epoch_type: "FOLLOW_UP",
        sequence_index: 4,
        duration_weeks: 8,
      },
    ],
    visits: [
      {
        id: "vis_imm_1",
        visit_name: "Screening (Day -21 to -1)",
        epoch_name: "Screening Phase",
        target_day: -10,
        window_lower_days: 10,
        window_upper_days: 0,
        is_mandatory: true,
      },
      {
        id: "vis_imm_2",
        visit_name: "Visit 1 / Baseline (Day 1)",
        epoch_name: "Main Treatment Epoch",
        target_day: 1,
        window_lower_days: 0,
        window_upper_days: 1,
        is_mandatory: true,
      },
      {
        id: "vis_imm_3",
        visit_name: "Visit 2 / Week 2 (Day 14)",
        epoch_name: "Main Treatment Epoch",
        target_day: 14,
        window_lower_days: 2,
        window_upper_days: 2,
        is_mandatory: true,
      },
      {
        id: "vis_imm_4",
        visit_name: "Visit 3 / Week 6 (Day 42)",
        epoch_name: "Main Treatment Epoch",
        target_day: 42,
        window_lower_days: 3,
        window_upper_days: 3,
        is_mandatory: true,
      },
      {
        id: "vis_imm_5",
        visit_name: "Visit 4 / Week 12 Primary Endpoint (Day 84)",
        epoch_name: "Main Treatment Epoch",
        target_day: 84,
        window_lower_days: 5,
        window_upper_days: 5,
        is_mandatory: true,
      },
      {
        id: "vis_imm_6",
        visit_name: "Safety Follow-up (Day 112)",
        epoch_name: "Safety Monitoring Follow-up",
        target_day: 112,
        window_lower_days: 7,
        window_upper_days: 7,
        is_mandatory: true,
      },
    ],
    activities: [
      {
        id: "act_imm_1",
        activity_name: "Informed Consent Form",
        cdash_domain: "DM",
        biomedical_concept_code: "C16468",
        assigned_visit_names: ["Screening (Day -21 to -1)"],
      },
      {
        id: "act_imm_2",
        activity_name: "Vital Signs & Physical Exam",
        cdash_domain: "VS",
        biomedical_concept_code: "C154628",
        assigned_visit_names: [
          "Screening (Day -21 to -1)",
          "Visit 1 / Baseline (Day 1)",
          "Visit 2 / Week 2 (Day 14)",
          "Visit 3 / Week 6 (Day 42)",
          "Visit 4 / Week 12 Primary Endpoint (Day 84)",
          "Safety Follow-up (Day 112)",
        ],
      },
      {
        id: "act_imm_3",
        activity_name: "Tender/Swollen Joint Count (DAS28 / 66/68)",
        cdash_domain: "PE",
        biomedical_concept_code: "C20989",
        assigned_visit_names: [
          "Screening (Day -21 to -1)",
          "Visit 1 / Baseline (Day 1)",
          "Visit 3 / Week 6 (Day 42)",
          "Visit 4 / Week 12 Primary Endpoint (Day 84)",
        ],
      },
      {
        id: "act_imm_4",
        activity_name: "Inflammatory Markers (hsCRP & ESR) & Safety Labs",
        cdash_domain: "LB",
        biomedical_concept_code: "C25294",
        assigned_visit_names: [
          "Screening (Day -21 to -1)",
          "Visit 1 / Baseline (Day 1)",
          "Visit 2 / Week 2 (Day 14)",
          "Visit 4 / Week 12 Primary Endpoint (Day 84)",
          "Safety Follow-up (Day 112)",
        ],
      },
      {
        id: "act_imm_5",
        activity_name: "HAQ-DI & Patient Global Assessment VAS",
        cdash_domain: "QS",
        biomedical_concept_code: "C17048",
        assigned_visit_names: [
          "Visit 1 / Baseline (Day 1)",
          "Visit 3 / Week 6 (Day 42)",
          "Visit 4 / Week 12 Primary Endpoint (Day 84)",
        ],
      },
      {
        id: "act_imm_6",
        activity_name: "Safety ECG Screening",
        cdash_domain: "EG",
        biomedical_concept_code: "C38053",
        assigned_visit_names: [
          "Screening (Day -21 to -1)",
          "Visit 4 / Week 12 Primary Endpoint (Day 84)",
        ],
      },
    ],
    criteria: [
      {
        id: "crit_imm_1",
        criterion_type: "INCLUSION",
        identifier: "INC-01",
        text_expression:
          "Diagnosis of moderate-to-severe Rheumatoid Arthritis for >= 6 months.",
        logical_expression:
          "MH.DIAGNOSIS_RA == TRUE && MH.DURATION_MONTHS >= 6",
      },
      {
        id: "crit_imm_2",
        criterion_type: "INCLUSION",
        identifier: "INC-02",
        text_expression:
          "Active disease defined by >= 6 tender joints and >= 6 swollen joints.",
        logical_expression:
          "PE.TENDER_JOINT_COUNT >= 6 && PE.SWOLLEN_JOINT_COUNT >= 6",
      },
      {
        id: "crit_imm_3",
        criterion_type: "EXCLUSION",
        identifier: "EXC-01",
        text_expression:
          "Active tuberculosis or history of untreated latent TB infection.",
        logical_expression: "LB.TB_QUANTIFERON == 'POSITIVE'",
      },
    ],
  },

  vaccines: {
    id: "vaccines",
    title: "Vaccines / Infectious Disease",
    category: "Vaccines",
    description:
      "Infectious disease immunization trial template with prime-booster schedules, neutralizing antibody titers, and electronic reactogenicity diary.",
    icon: "check-circle",
    defaultPhase: "PHASE_I",
    defaultRandomizationRatio: "1:1:1",
    protocol_id: "CDNC-2026-VAC",
    study_title:
      "A Phase I/II Randomized Double-Blind Study to Evaluate the Safety and Immunogenicity of Novel mRNA Vaccine Against Respiratory Viruses",
    confidence_score: 0.99,
    arms: [
      {
        id: "arm_vac_1",
        name: "Investigational Vaccine High Dose (100mcg)",
        arm_type: "EXPERIMENTAL",
        description:
          "High-dose formulation administered as 2-dose prime-boost series on Day 1 and Day 29",
        target_sample_size: 300,
      },
      {
        id: "arm_vac_2",
        name: "Investigational Vaccine Low Dose (30mcg)",
        arm_type: "EXPERIMENTAL",
        description: "Low-dose formulation administered on Day 1 and Day 29",
        target_sample_size: 300,
      },
      {
        id: "arm_vac_3",
        name: "Licensed Active Comparator Vaccine",
        arm_type: "ACTIVE_COMPARATOR",
        description: "Approved seasonal reference vaccine control",
        target_sample_size: 300,
      },
    ],
    epochs: [
      {
        id: "ep_vac_1",
        name: "Screening & Prime Immunization",
        epoch_type: "SCREENING",
        sequence_index: 1,
        duration_weeks: 4,
      },
      {
        id: "ep_vac_2",
        name: "Booster Dose & Primary Immunogenicity",
        epoch_type: "TREATMENT",
        sequence_index: 2,
        duration_weeks: 8,
      },
      {
        id: "ep_vac_3",
        name: "Washout Epoch",
        epoch_type: "WASHOUT",
        sequence_index: 3,
        duration_weeks: 4,
      },
      {
        id: "ep_vac_4",
        name: "Long-Term Safety Follow-up",
        epoch_type: "FOLLOW_UP",
        sequence_index: 4,
        duration_weeks: 24,
      },
    ],
    visits: [
      {
        id: "vis_vac_1",
        visit_name: "Visit 1 / Screening & Vaccination 1 (Day 1)",
        epoch_name: "Screening & Prime Immunization",
        target_day: 1,
        window_lower_days: 0,
        window_upper_days: 0,
        is_mandatory: true,
      },
      {
        id: "vis_vac_2",
        visit_name: "Visit 2 / Post-Prime Call (Day 8)",
        epoch_name: "Screening & Prime Immunization",
        target_day: 8,
        window_lower_days: 1,
        window_upper_days: 2,
        is_mandatory: true,
      },
      {
        id: "vis_vac_3",
        visit_name: "Visit 3 / Booster Dose Vaccination 2 (Day 29)",
        epoch_name: "Booster Dose & Primary Immunogenicity",
        target_day: 29,
        window_lower_days: 2,
        window_upper_days: 2,
        is_mandatory: true,
      },
      {
        id: "vis_vac_4",
        visit_name: "Visit 4 / Primary Serology Evaluation (Day 57)",
        epoch_name: "Booster Dose & Primary Immunogenicity",
        target_day: 57,
        window_lower_days: 3,
        window_upper_days: 3,
        is_mandatory: true,
      },
      {
        id: "vis_vac_5",
        visit_name: "Long-Term Safety Follow-up (Day 180)",
        epoch_name: "Long-Term Safety Follow-up",
        target_day: 180,
        window_lower_days: 7,
        window_upper_days: 7,
        is_mandatory: true,
      },
    ],
    activities: [
      {
        id: "act_vac_1",
        activity_name: "Informed Consent Form",
        cdash_domain: "DM",
        biomedical_concept_code: "C16468",
        assigned_visit_names: ["Visit 1 / Screening & Vaccination 1 (Day 1)"],
      },
      {
        id: "act_vac_2",
        activity_name: "Vital Signs & Targeted Injection Site Exam",
        cdash_domain: "VS",
        biomedical_concept_code: "C154628",
        assigned_visit_names: [
          "Visit 1 / Screening & Vaccination 1 (Day 1)",
          "Visit 3 / Booster Dose Vaccination 2 (Day 29)",
          "Visit 4 / Primary Serology Evaluation (Day 57)",
          "Long-Term Safety Follow-up (Day 180)",
        ],
      },
      {
        id: "act_vac_3",
        activity_name: "7-Day E-Diary Reactogenicity Questionnaire",
        cdash_domain: "QS",
        biomedical_concept_code: "C17048",
        assigned_visit_names: [
          "Visit 1 / Screening & Vaccination 1 (Day 1)",
          "Visit 2 / Post-Prime Call (Day 8)",
          "Visit 3 / Booster Dose Vaccination 2 (Day 29)",
        ],
      },
      {
        id: "act_vac_4",
        activity_name: "Neutralizing Antibody & Serology Panel",
        cdash_domain: "LB",
        biomedical_concept_code: "C25294",
        assigned_visit_names: [
          "Visit 1 / Screening & Vaccination 1 (Day 1)",
          "Visit 3 / Booster Dose Vaccination 2 (Day 29)",
          "Visit 4 / Primary Serology Evaluation (Day 57)",
          "Long-Term Safety Follow-up (Day 180)",
        ],
      },
      {
        id: "act_vac_5",
        activity_name: "Physical Exam & Local Site Reaction Review",
        cdash_domain: "PE",
        biomedical_concept_code: "C20989",
        assigned_visit_names: [
          "Visit 1 / Screening & Vaccination 1 (Day 1)",
          "Visit 3 / Booster Dose Vaccination 2 (Day 29)",
          "Long-Term Safety Follow-up (Day 180)",
        ],
      },
    ],
    criteria: [
      {
        id: "crit_vac_1",
        criterion_type: "INCLUSION",
        identifier: "INC-01",
        text_expression:
          "Healthy adults aged 18 to 55 years at time of immunization.", // deid-ignore
        logical_expression: "DM.AGE >= 18 && DM.AGE <= 55",
      },
      {
        id: "crit_vac_2",
        criterion_type: "INCLUSION",
        identifier: "INC-02",
        text_expression:
          "Agrees to complete 7-day electronic reactogenicity diary after each dose.",
        logical_expression: "QS.EDIARY_AGREEMENT == TRUE",
      },
      {
        id: "crit_vac_3",
        criterion_type: "EXCLUSION",
        identifier: "EXC-01",
        text_expression:
          "History of severe adverse reaction or anaphylaxis to vaccines.",
        logical_expression: "MH.ANAPHYLAXIS_VACCINE == TRUE",
      },
    ],
  },
};
