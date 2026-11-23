import { getBearerToken } from "../authorization.js";
import { ApiError, getBaseUrl, unwrapResponse } from "../client";
import { describeKernelError, kernelClient, mutationHeaders } from "./shared";
import type {
  KernelProtocolDraftSummary,
  KernelProtocolImportRequest,
} from "./studies";

export async function executeProtocolDraftImport({
  studyRef,
  file,
  content,
  fileName,
  format,
  templateIds,
  replaceForms = false,
  amendmentClassification,
  transitionPolicy,
  reason,
  idempotencyKey,
}: KernelProtocolImportRequest): Promise<KernelProtocolDraftSummary> {
  if (file) {
    const baseUrl = getBaseUrl().replace(/\/+$/, "");
    const url = `${baseUrl}/api/v1/kernel/studies/${encodeURIComponent(studyRef)}/protocol-imports`;
    const formData = new FormData();
    formData.append("file", file);
    if (templateIds && templateIds.length > 0) {
      for (const tplId of templateIds) {
        formData.append("template_ids", tplId);
      }
    }
    if (replaceForms) {
      formData.append("replace_forms", "true");
    }
    if (amendmentClassification) {
      formData.append("amendment_classification", amendmentClassification);
    }
    if (transitionPolicy) {
      formData.append("transition_policy", transitionPolicy);
    }

    const headers: Record<string, string> = mutationHeaders(
      reason,
      idempotencyKey
    );
    const token = await getBearerToken();
    if (token) {
      headers["Authorization"] = `Bearer ${token}`;
    }

    const response = await globalThis.fetch(url, {
      method: "POST",
      headers,
      body: formData,
    });
    if (!response.ok) {
      const errData = await response.json().catch(() => null);
      const tempError = new ApiError(
        `Import failed with status ${response.status}`,
        response.status,
        response.statusText,
        errData
      );
      throw new ApiError(
        describeKernelError(tempError),
        response.status,
        response.statusText,
        errData
      );
    }
    return (await response.json()) as KernelProtocolDraftSummary;
  }

  return unwrapResponse(
    kernelClient.POST("/api/v1/kernel/studies/{studyRef}/protocol-imports", {
      params: { path: { studyRef } },
      body: {
        content: content ?? null,
        file_name: fileName ?? null,
        format: format ?? null,
        template_ids:
          templateIds && templateIds.length > 0 ? templateIds : null,
        replace_forms: replaceForms,
        amendment_classification: amendmentClassification ?? null,
        transition_policy: transitionPolicy ?? null,
      },
      headers: mutationHeaders(reason, idempotencyKey),
    })
  );
}
