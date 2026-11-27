import { computed, defineComponent, h } from "vue";
import { ClinicalContextBar } from "ui";

const WORKFLOW_CONTEXT_LABELS = Object.freeze({
  ecrf: "Data collection",
  observation: "Observation",
  ctms: "Site readiness",
  ssu: "Site startup",
  etmf: "Regulatory documents",
  documents: "Controlled documents",
  training: "Protocol training",
  tickets: "Support",
  knowledge: "Knowledge",
  mdr: "Protocol design",
  digitization: "Protocol digitization",
  "icf-builder": "Informed consent",
  "econsent-authoring": "Consent authoring",
  "amendment-diff": "Protocol amendments",
  imaging: "Medical imaging",
  devices: "Medical devices",
  "device-investigation": "Device investigation",
  adjudication: "Endpoint adjudication",
  cec: "Endpoint adjudication",
  audit: "Audit ledger",
  auditor: "Inspection review",
  rules: "Validation rules",
  coding: "Medical coding",
  lakehouse: "Clinical analytics",
  sensors: "Connected sensors",
  labs: "Laboratory governance",
  dct: "Decentralized visits",
  safety: "Safety review",
  quality: "Quality management",
  notifications: "Notifications",
  "participant-wallet": "Participant reimbursements",
  "lay-summaries": "Plain-language summaries",
});

export default defineComponent({
  name: "ScopedClinicalContextBar",
  props: {
    studyCode: { type: String, default: "" },
    workspaceScope: { type: Object, required: true },
    routeName: { type: String, default: "" },
    routePath: { type: String, default: "" },
  },
  setup(props) {
    const currentLabel = computed(() => {
      if (WORKFLOW_CONTEXT_LABELS[props.routeName]) {
        return WORKFLOW_CONTEXT_LABELS[props.routeName];
      }
      const cleanPath = String(props.routePath || "").replace(/^\//, "");
      return WORKFLOW_CONTEXT_LABELS[cleanPath] || "";
    });

    const studyTo = computed(() =>
      props.studyCode ? `/studies/${encodeURIComponent(props.studyCode)}` : ""
    );

    const siteTo = computed(() => {
      const siteId = props.workspaceScope?.siteId;
      if (!siteId) return "";
      if (!props.studyCode)
        return `/studies?site=${encodeURIComponent(siteId)}`;
      return {
        path: `/studies/${encodeURIComponent(props.studyCode)}`,
        query: {
          stage: "conduct",
          section: "participants",
          site: siteId,
        },
      };
    });

    const participantTo = computed(() => {
      const siteId = props.workspaceScope?.siteId;
      const participant =
        props.workspaceScope?.participantRef ||
        props.workspaceScope?.subjectCode;
      if (!participant) return "";
      if (!props.studyCode) {
        return `/studies?participant=${encodeURIComponent(participant)}`;
      }
      return {
        path: `/studies/${encodeURIComponent(props.studyCode)}`,
        query: {
          stage: "conduct",
          ...(siteId ? { site: siteId } : {}),
          participant,
        },
      };
    });

    return () =>
      h(ClinicalContextBar, {
        studiesTo: "/studies",
        studyCode: props.studyCode,
        studyTo: studyTo.value,
        siteCode: props.workspaceScope?.siteId || "",
        siteTo: siteTo.value,
        participantCode: props.workspaceScope?.subjectCode || "",
        participantTo: participantTo.value,
        currentLabel: currentLabel.value,
      });
  },
});
