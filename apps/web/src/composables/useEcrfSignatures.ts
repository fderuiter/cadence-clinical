import { computed, ref, type Ref } from "vue";
import { formatObservationValue } from "../api/observationValue";
import type {
  EcrfForm,
  EcrfFormItem,
  EcrfFormSignedValue,
  EcrfSignatureMeaning,
} from "../api/ecrfFormsApi";
import { canSignForms } from "../canvas/authority";
import { useAuthStore } from "../stores/auth.js";

export const ECRF_INVESTIGATOR_REASONS = [
  "Investigator Certification",
  "INVESTIGATOR_SIGNATURE",
  "APPROVAL",
  "CLINICAL_QC",
  "REVIEW",
];

const MEANING_LABELS: Record<string, string> = {
  approved: "Approved",
  responsibility: "Responsibility",
};

export function meaningLabel(meaning: string): string {
  return MEANING_LABELS[meaning.toLowerCase()] ?? meaning;
}

/**
 * 21 CFR Part 11 eCRF form attestation state, authority resolution, superseded
 * value diff helpers, and SignatureCaptureModal step-up callback (#5463).
 */
export function useEcrfSignatures(options: {
  form: Ref<EcrfForm | null>;
  items: Ref<EcrfFormItem[]>;
  dirty: Ref<boolean>;
  signing: Ref<boolean>;
  sign: (
    meaning: EcrfSignatureMeaning,
    sigToken: string,
    signingReason: string
  ) => Promise<{ kind: string; message?: string } | null | undefined>;
}) {
  const { form, items, dirty, signing, sign } = options;
  const authStore = useAuthStore();

  const signMeaning = ref<EcrfSignatureMeaning>("approved");
  const showSignModal = ref(false);

  const hasSignAuthority = computed(() => {
    if (typeof form.value?.can_sign === "boolean") {
      return form.value.can_sign;
    }
    return authStore.isAuthenticated ? canSignForms(authStore) : true;
  });

  const signerUsername = computed(() => authStore.identity?.username ?? "");
  const signerFullName = computed(() => {
    const identity = authStore.identity;
    if (!identity) return "";
    const fullName = [identity.firstName, identity.lastName]
      .filter(Boolean)
      .join(" ")
      .trim();
    return fullName || identity.username || "";
  });

  /** Why the form cannot be signed now, or null when it can. */
  const signHint = computed(() => {
    if (!form.value || signing.value) return null;
    if (dirty.value) return "Save or discard your changes before signing.";
    if (!form.value.content_hash) return "The form has no values to sign yet.";
    return null;
  });

  function currentOf(itemId: string): EcrfFormItem | undefined {
    return items.value.find((i) => i.item_id === itemId);
  }

  function currentValueText(itemId: string): string {
    const value = currentOf(itemId)?.value;
    return value ? formatObservationValue(value) : "No value";
  }

  function changedSince(signed: EcrfFormSignedValue): boolean {
    const current = currentOf(signed.item_id);
    return !current || Number(current.revision) !== Number(signed.revision);
  }

  /** The signature dialog's callback: it has the step-up token and the signing reason the user chose. */
  async function signWithToken(sigToken: string, signingReason: string) {
    const result = await sign(signMeaning.value, sigToken, signingReason);
    if (!result || result.kind !== "signed") {
      throw new Error(result?.message ?? "The form could not be signed.");
    }
    return result;
  }

  return {
    ECRF_INVESTIGATOR_REASONS,
    signMeaning,
    showSignModal,
    hasSignAuthority,
    signerUsername,
    signerFullName,
    meaningLabel,
    signHint,
    currentOf,
    currentValueText,
    changedSince,
    signWithToken,
  };
}
