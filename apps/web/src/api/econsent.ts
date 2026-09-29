import { openApiClient, unwrapResponse } from "./client";
import type { paths } from "./types";

export type ClauseCreateBody =
  paths["/api/v1/econsent/clauses"]["post"]["requestBody"]["content"]["application/json"];
export type ClauseUpdateBody =
  paths["/api/v1/econsent/clauses/{clause_id}"]["put"]["requestBody"]["content"]["application/json"];
export type TemplateCreateBody =
  paths["/api/v1/econsent/templates"]["post"]["requestBody"]["content"]["application/json"];
export type TemplateUpdateBody =
  paths["/api/v1/econsent/templates/{template_id}"]["put"]["requestBody"]["content"]["application/json"];
export type TranslationCreateBody =
  paths["/api/v1/econsent/translations"]["post"]["requestBody"]["content"]["application/json"];
export type TranslationUpdateBody =
  paths["/api/v1/econsent/translations/{translation_id}"]["put"]["requestBody"]["content"]["application/json"];

export const econsentService = {
  // --- Clauses ---
  createClause(body: ClauseCreateBody, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/econsent/clauses", {
        body,
        ...options,
      })
    );
  },

  updateClause(
    clauseId: string,
    body: ClauseUpdateBody,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.PUT("/api/v1/econsent/clauses/{clause_id}", {
        params: { path: { clause_id: clauseId } },
        body,
        ...options,
      })
    );
  },

  listClauses(
    params: {
      study_id?: string;
      clause_id?: string;
      all_versions?: boolean;
    } = {},
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/econsent/clauses", {
        params: {
          query: {
            study_id: params.study_id,
            clause_id: params.clause_id,
            all_versions: params.all_versions,
          },
        },
        ...options,
      })
    );
  },

  getClause(
    clauseId: string,
    params: { version_index?: number } = {},
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/econsent/clauses/{clause_id}", {
        params: {
          path: { clause_id: clauseId },
          query: { version_index: params.version_index },
        },
        ...options,
      })
    );
  },

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

  getTemplate(
    templateId: string,
    params: { version_index?: number } = {},
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/econsent/templates/{template_id}", {
        params: {
          path: { template_id: templateId },
          query: { version_index: params.version_index },
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
      openApiClient.POST("/api/v1/econsent/templates/{template_id}/publish", {
        params: { path: { template_id: templateId } },
        ...options,
      })
    );
  },

  // --- Comprehension Checks ---
  defineComprehensionCheck(
    templateId: string,
    versionIndex: number,
    body: unknown,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST(
        "/api/v1/econsent/templates/{template_id}/versions/{version_index}/comprehension-checks",
        {
          params: {
            path: {
              template_id: templateId,
              version_index: versionIndex,
            },
          },
          body: body as never,
          ...options,
        }
      )
    );
  },

  getComprehensionCheck(
    templateId: string,
    versionIndex: number,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.GET(
        "/api/v1/econsent/templates/{template_id}/versions/{version_index}/comprehension-checks",
        {
          params: {
            path: {
              template_id: templateId,
              version_index: versionIndex,
            },
          },
          ...options,
        }
      )
    );
  },

  // --- Translations ---
  createTranslation(
    body: TranslationCreateBody,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/econsent/translations", {
        body,
        ...options,
      })
    );
  },

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

  listTranslations(
    params: {
      source_id?: string;
      source_type?: string;
      language_code?: string;
      status?: string;
      all_versions?: boolean;
    } = {},
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/econsent/translations", {
        params: {
          query: {
            source_id: params.source_id,
            source_type: params.source_type,
            language_code: params.language_code,
            status: params.status,
            all_versions: params.all_versions,
          },
        },
        ...options,
      })
    );
  },

  getTranslation(
    translationId: string,
    params: { version_index?: number } = {},
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/econsent/translations/{translation_id}", {
        params: {
          path: { translation_id: translationId },
          query: { version_index: params.version_index },
        },
        ...options,
      })
    );
  },

  transitionTranslation(
    translationId: string,
    body: unknown,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST(
        "/api/v1/econsent/translations/{translation_id}/transition",
        {
          params: { path: { translation_id: translationId } },
          body: body as never,
          ...options,
        }
      )
    );
  },

  // --- Approved Composed Content (Participant) ---
  getApprovedContent(
    templateId: string,
    params: { language_code?: string; version_index?: number } = {},
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.GET(
        "/api/v1/econsent/templates/{template_id}/approved-content",
        {
          params: {
            path: { template_id: templateId },
            query: {
              language_code: params.language_code || "en",
              version_index: params.version_index ?? null,
            },
          },
          ...options,
        }
      )
    );
  },

  // --- Comprehension Evaluation & Signature Capture ---
  submitAnswers(
    templateId: string,
    versionIndex: number,
    body: unknown,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST(
        "/api/v1/econsent/templates/{template_id}/versions/{version_index}/submit-answers",
        {
          params: {
            path: {
              template_id: templateId,
              version_index: versionIndex,
            },
          },
          body: body as never,
          ...options,
        }
      )
    );
  },

  signTemplate(
    templateId: string,
    versionIndex: number,
    body: unknown,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST(
        "/api/v1/econsent/templates/{template_id}/versions/{version_index}/sign",
        {
          params: {
            path: {
              template_id: templateId,
              version_index: versionIndex,
            },
          },
          body: body as never,
          ...options,
        }
      )
    );
  },

  captureConsent(
    templateId: string,
    versionIndex: number,
    body: unknown,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST(
        "/api/v1/econsent/templates/{template_id}/versions/{version_index}/capture-consent",
        {
          params: {
            path: {
              template_id: templateId,
              version_index: versionIndex,
            },
          },
          body: body as never,
          ...options,
        }
      )
    );
  },

  getSubjectConsentStatus(
    subjectPseudonym: string,
    params: { study_id?: string } = {},
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.GET(
        "/api/v1/econsent/subjects/{subject_pseudonym}/consent-status",
        {
          params: {
            path: { subject_pseudonym: subjectPseudonym || "" },
            query: { study_id: (params.study_id ?? null) as unknown as string },
          },
          ...options,
        }
      )
    );
  },

  diffTemplateVersions(
    templateId: string,
    baseVersion: number,
    targetVersion: number,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.GET(
        "/api/v1/econsent/templates/{template_id}/diff/{base_version}/{target_version}",
        {
          params: {
            path: {
              template_id: templateId,
              base_version: baseVersion,
              target_version: targetVersion,
            },
          },
          ...options,
        }
      )
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

  harmonizeClause(
    clauseId: string,
    body: unknown,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/econsent/clauses/{clause_id}/harmonize", {
        params: { path: { clause_id: clauseId } },
        body: body as never,
        ...options,
      })
    );
  },
};
