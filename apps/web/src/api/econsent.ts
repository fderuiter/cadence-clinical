import { createOpenApiClient, unwrapResponse } from "./client";
import type { paths as EconsentPaths } from "./generated/econsent";
import { openApiClient } from "./legacyClient";
import type { paths } from "./types";

const econsentClient = createOpenApiClient<EconsentPaths>();

export type TemplateCreateBody =
  paths["/api/v1/econsent/templates"]["post"]["requestBody"]["content"]["application/json"];
export type TemplateUpdateBody =
  paths["/api/v1/econsent/templates/{template_id}"]["put"]["requestBody"]["content"]["application/json"];
export type TranslationUpdateBody =
  paths["/api/v1/econsent/translations/{translation_id}"]["put"]["requestBody"]["content"]["application/json"];

export const ECONSENT_CAPTURE_SEMANTIC_ACTION = "econsent.consent.capture";

export function econsentCaptureAction(
  templateId: string,
  versionIndex = 1
): string {
  return `/api/v1/econsent/templates/${encodeURIComponent(templateId)}/versions/${versionIndex}/capture-consent`;
}

export const econsentService = {
  // --- Templates ---
  createTemplate(
    body: TemplateCreateBody,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/econsent/templates", {
        body,
        ...options,
      })
    );
  },

  updateTemplate(
    templateId: string,
    body: TemplateUpdateBody,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.PUT("/api/v1/econsent/templates/{template_id}", {
        params: { path: { template_id: templateId } },
        body,
        ...options,
      })
    );
  },

  listTemplates(
    params: {
      study_id?: string;
      template_id?: string;
      all_versions?: boolean;
    } = {},
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/econsent/templates", {
        params: {
          query: {
            study_id: params.study_id,
            template_id: params.template_id,
            all_versions: params.all_versions,
          },
        },
        ...options,
      })
    );
  },

  composeTemplate(
    templateId: string,
    params: { version_index?: number } = {},
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/econsent/templates/{template_id}/compose", {
        params: {
          path: { template_id: templateId },
          query: { version_index: params.version_index },
        },
        ...options,
      })
    );
  },

  publishTemplate(templateId: string, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      econsentClient.POST("/api/v1/econsent/templates/{template_id}/publish", {
        params: { path: { template_id: templateId } },
        ...options,
      })
    );
  },

  // --- Translations ---
  updateTranslation(
    translationId: string,
    body: TranslationUpdateBody,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.PUT("/api/v1/econsent/translations/{translation_id}", {
        params: { path: { translation_id: translationId } },
        body,
        ...options,
      })
    );
  },

  // --- Readability & Jargon Harmonization ---
  analyzeReadability(body: unknown, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/econsent/readability/analyze", {
        body: body as never,
        ...options,
      })
    );
  },

  harmonizeReadability(body: unknown, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/econsent/readability/harmonize", {
        body: body as never,
        ...options,
      })
    );
  },
};
