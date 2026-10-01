import { defineStore } from "pinia";
import { apiClient } from "../api/apiClient";

export const useCodingStore = defineStore("coding", {
  state: () => ({
    assignments: [],
    selectedAssignmentIds: [],
    selectedClusterKeys: [],
    viewMode: "INDIVIDUAL", // "INDIVIDUAL" | "CLUSTERS"
    activeAssignment: null,
    dictionarySearchResults: [],
    searchStatus: null,
    isSearching: false,
    isLoading: false,
    error: null,
    filters: {
      status: "ALL",
      dictionaryType: "ALL",
      confidenceThreshold: "ALL", // "ALL" | "HIGH" (>=90%) | "MEDIUM" (70-89%) | "LOW" (<70%)
      search: "",
    },
    impactAnalysis: {
      isOpen: false,
      isLoading: false,
      dictionaryType: "MEDDRA",
      targetVersion: "27.0",
      results: null,
      error: null,
    },
    browserModal: {
      isOpen: false,
      assignment: null,
      dictionaryType: "MEDDRA",
      searchTerm: "",
      targetLevel: "LLT",
      version: "26.0",
    },
  }),

  getters: {
    getItemConfidenceScore: () => (item) => {
      if (item.suggestions && Array.isArray(item.suggestions) && item.suggestions.length > 0) {
        const score = item.suggestions[0].score ?? item.score ?? 0;
        return score > 1 ? score / 100 : score;
      }
      const score = item.score ?? 0;
      return score > 1 ? score / 100 : score;
    },

    filteredAssignments: (state) => {
      return state.assignments.filter((item) => {
        // Filter by Status
        if (state.filters.status !== "ALL") {
          if (state.filters.status === "CODED_ALL") {
            if (item.status !== "CODED" && item.status !== "AUTO_CODED") {
              return false;
            }
          } else if (item.status !== state.filters.status) {
            return false;
          }
        }

        // Filter by Dictionary Type
        if (state.filters.dictionaryType !== "ALL") {
          if (
            (item.dictionary_type || "").toUpperCase() !==
            state.filters.dictionaryType.toUpperCase()
          ) {
            return false;
          }
        }

        // Filter by Confidence Threshold
        if (
          state.filters.confidenceThreshold &&
          state.filters.confidenceThreshold !== "ALL"
        ) {
          const score = state.getItemConfidenceScore(item);

          if (state.filters.confidenceThreshold === "HIGH" && score < 0.90) {
            return false;
          }
          if (
            state.filters.confidenceThreshold === "MEDIUM" &&
            (score < 0.70 || score >= 0.90)
          ) {
            return false;
          }
          if (state.filters.confidenceThreshold === "LOW" && score >= 0.70) {
            return false;
          }
        }

        // Filter by search query
        if (state.filters.search && state.filters.search.trim()) {
          const q = state.filters.search.toLowerCase().trim();
          const matchVerbatim = (item.verbatim_text || "")
            .toLowerCase()
            .includes(q);
          const matchCode = (item.coded_code || "").toLowerCase().includes(q);
          const matchTerm = (item.coded_term || "").toLowerCase().includes(q);
          const matchField = (item.source_field || "")
            .toLowerCase()
            .includes(q);
          const matchObs = (item.observation_id || "")
            .toLowerCase()
            .includes(q);
          const matchSug = item.suggestions && item.suggestions.some(
            (s) =>
              (s.term || s.pt_name || s.preferred_name || "").toLowerCase().includes(q) ||
              (s.code || "").toLowerCase().includes(q)
          );
          if (
            !matchVerbatim &&
            !matchCode &&
            !matchTerm &&
            !matchField &&
            !matchObs &&
            !matchSug
          ) {
            return false;
          }
        }

        return true;
      });
    },

    verbatimClusters: (state) => {
      const items = state.assignments.filter((item) => {
        // Filter by Status
        if (state.filters.status !== "ALL") {
          if (state.filters.status === "CODED_ALL") {
            if (item.status !== "CODED" && item.status !== "AUTO_CODED") {
              return false;
            }
          } else if (item.status !== state.filters.status) {
            return false;
          }
        }

        // Filter by Dictionary Type
        if (state.filters.dictionaryType !== "ALL") {
          if (
            (item.dictionary_type || "").toUpperCase() !==
            state.filters.dictionaryType.toUpperCase()
          ) {
            return false;
          }
        }

        // Filter by Confidence Threshold
        if (
          state.filters.confidenceThreshold &&
          state.filters.confidenceThreshold !== "ALL"
        ) {
          const score = state.getItemConfidenceScore(item);

          if (state.filters.confidenceThreshold === "HIGH" && score < 0.90) {
            return false;
          }
          if (
            state.filters.confidenceThreshold === "MEDIUM" &&
            (score < 0.70 || score >= 0.90)
          ) {
            return false;
          }
          if (state.filters.confidenceThreshold === "LOW" && score >= 0.70) {
            return false;
          }
        }

        // Filter by search query
        if (state.filters.search && state.filters.search.trim()) {
          const q = state.filters.search.toLowerCase().trim();
          const matchVerbatim = (item.verbatim_text || "")
            .toLowerCase()
            .includes(q);
          const matchCode = (item.coded_code || "").toLowerCase().includes(q);
          const matchTerm = (item.coded_term || "").toLowerCase().includes(q);
          const matchField = (item.source_field || "")
            .toLowerCase()
            .includes(q);
          const matchObs = (item.observation_id || "")
            .toLowerCase()
            .includes(q);
          const matchSug = item.suggestions && item.suggestions.some(
            (s) =>
              (s.term || s.pt_name || s.preferred_name || "").toLowerCase().includes(q) ||
              (s.code || "").toLowerCase().includes(q)
          );
          if (
            !matchVerbatim &&
            !matchCode &&
            !matchTerm &&
            !matchField &&
            !matchObs &&
            !matchSug
          ) {
            return false;
          }
        }

        return true;
      });

      const clusterMap = new Map();

      for (const item of items) {
        const normText = (item.verbatim_text || "").trim().toLowerCase();
        const dictType = (item.dictionary_type || "MEDDRA").toUpperCase();

        let topSug = null;
        let sugCode = "";
        let sugTerm = "";
        if (item.suggestions && Array.isArray(item.suggestions) && item.suggestions.length > 0) {
          topSug = item.suggestions[0];
          sugCode = topSug.code || topSug.drug_code || topSug.pt_code || "10019211";
          sugTerm = topSug.pt_name || topSug.preferred_name || topSug.term || topSug.drug_name || "Headache";
        } else if (item.coded_code) {
          sugCode = item.coded_code;
          sugTerm = item.coded_term || "";
        }

        const sugKey = sugCode ? `${sugCode}:${sugTerm.toLowerCase()}` : "uncoded";
        const key = `${dictType}:${normText}:${sugKey}`;

        let itemScore = 0;
        if (topSug) {
          const raw = topSug.score ?? item.score ?? 0.95;
          itemScore = raw > 1 ? raw / 100 : raw;
        } else if (item.score) {
          itemScore = item.score > 1 ? item.score / 100 : item.score;
        }

        if (!clusterMap.has(key)) {
          clusterMap.set(key, {
            clusterKey: key,
            verbatim_text: item.verbatim_text || "",
            dictionary_type: item.dictionary_type || "MEDDRA",
            dictionary_version: item.dictionary_version || "26.0",
            items: [],
            top_suggestion: (topSug || item.coded_code)
              ? {
                  code: sugCode || "10019211",
                  term: sugTerm || "Headache",
                  llt_name: topSug?.llt_name || topSug?.llt || topSug?.lowest_level_term || "",
                  score: itemScore,
                }
              : null,
            max_score: itemScore,
          });
        }

        const cluster = clusterMap.get(key);
        cluster.items.push(item);
        if (itemScore > cluster.max_score) {
          cluster.max_score = itemScore;
          if (cluster.top_suggestion) {
            cluster.top_suggestion.score = itemScore;
          }
        }
      }

      const result = [];
      for (const cluster of clusterMap.values()) {
        const instance_count = cluster.items.length;
        const blocked_items = cluster.items.filter(
          (a) => a.status === "QUERY_PENDING" || a.is_locked || a.is_blocked
        );
        const unblocked_items = cluster.items.filter(
          (a) => a.status !== "QUERY_PENDING" && !a.is_locked && !a.is_blocked
        );

        let confidence_category = "LOW";
        if (cluster.max_score >= 0.90) {
          confidence_category = "HIGH";
        } else if (cluster.max_score >= 0.70) {
          confidence_category = "MEDIUM";
        }

        let status = "UNCODED";
        if (cluster.items.every((a) => a.status === "CODED" || a.status === "AUTO_CODED")) {
          status = "CODED";
        } else if (cluster.items.some((a) => a.status === "QUERY_PENDING")) {
          status = "QUERY_PENDING";
        } else if (cluster.items.some((a) => a.status === "SUGGESTED")) {
          status = "SUGGESTED";
        }

        result.push({
          ...cluster,
          instance_count,
          unblocked_items,
          blocked_items,
          has_blocked_items: blocked_items.length > 0,
          unblocked_ids: unblocked_items.map((i) => i.id),
          confidence_category,
          status,
        });
      }

      return result;
    },

    totalClusterCount: (state) => state.verbatimClusters.length,

    totalInstanceCount: (state) =>
      state.verbatimClusters.reduce((sum, c) => sum + c.instance_count, 0),

    batchAutoCodingProgress: (state) => {
      if (state.assignments.length === 0) return 0;
      const codedCount = state.assignments.filter(
        (a) => a.status === "CODED" || a.status === "AUTO_CODED"
      ).length;
      return Math.round((codedCount / state.assignments.length) * 100);
    },

    selectedClusterCount: (state) => state.selectedClusterKeys.length,

    uncodedCount: (state) => {
      return state.assignments.filter((a) => a.status === "UNCODED").length;
    },

    suggestedCount: (state) => {
      return state.assignments.filter((a) => a.status === "SUGGESTED").length;
    },

    codedCount: (state) => {
      return state.assignments.filter(
        (a) => a.status === "CODED" || a.status === "AUTO_CODED"
      ).length;
    },

    queryPendingCount: (state) => {
      return state.assignments.filter((a) => a.status === "QUERY_PENDING")
        .length;
    },

    totalCount: (state) => state.assignments.length,

    selectedCount: (state) => state.selectedAssignmentIds.length,
  },

  actions: {
    async fetchAssignments(filterOverrides = {}) {
      this.isLoading = true;
      this.error = null;
      try {
        const params = new URLSearchParams();
        if (filterOverrides.status && filterOverrides.status !== "ALL") {
          params.append("status", filterOverrides.status);
        }
        if (
          filterOverrides.dictionary_type &&
          filterOverrides.dictionary_type !== "ALL"
        ) {
          params.append("dictionary_type", filterOverrides.dictionary_type);
        }
        if (filterOverrides.verbatim_text) {
          params.append("verbatim_text", filterOverrides.verbatim_text);
        }

        const queryString = params.toString();
        const url = `/api/v1/execution/coding/assignments${queryString ? "?" + queryString : ""}`;
        const data = await apiClient.get(url, {
          changeReason: "Fetch medical coding assignments queue",
        });

        this.assignments = Array.isArray(data) ? data : [];
      } catch (err) {
        this.error = err.message || "Failed to load medical coding queue";
        console.error("Error loading coding assignments:", err);
      } finally {
        this.isLoading = false;
      }
    },

    async searchDictionary({ term, dictionaryType, version, targetLevel }) {
      if (!term || !term.trim()) {
        this.dictionarySearchResults = [];
        this.searchStatus = null;
        return;
      }

      this.isSearching = true;
      this.error = null;
      try {
        const dictTypeUpper = (dictionaryType || "MEDDRA").toUpperCase();
        let url = "";
        if (dictTypeUpper === "MEDDRA") {
          const p = new URLSearchParams({
            term: term.trim(),
            version: version || "26.0",
          });
          if (targetLevel) {
            p.append("target_level", targetLevel);
          }
          url = `/api/v1/dictionaries/meddra/code?${p.toString()}`;
        } else {
          const p = new URLSearchParams({
            term: term.trim(),
            version: version || "2024-03",
          });
          url = `/api/v1/dictionaries/whodrug/code?${p.toString()}`;
        }

        const res = await apiClient.get(url, {
          changeReason: `Dictionary search ${dictTypeUpper} for "${term}"`,
        });

        this.searchStatus = res.status || "UNCODABLE";
        this.dictionarySearchResults = res.matches || [];
      } catch (err) {
        this.error = err.message || "Dictionary lookup failed";
        this.dictionarySearchResults = [];
        this.searchStatus = "ERROR";
      } finally {
        this.isSearching = false;
      }
    },

    async applyAction({
      assignmentId,
      action,
      code,
      term,
      suggestionIndex,
      reasonForChange,
    }) {
      this.isLoading = true;
      this.error = null;
      try {
        const payload = {
          action,
          code,
          term,
          suggestion_index: suggestionIndex,
          reason_for_change:
            reasonForChange || `Applied coding decision: ${action}`,
        };

        const updated = await apiClient.post(
          `/api/v1/execution/coding/assignments/${assignmentId}/action`,
          payload,
          {
            changeReason: payload.reason_for_change,
          }
        );

        // Update in state
        const idx = this.assignments.findIndex((a) => a.id === assignmentId);
        if (idx !== -1) {
          this.assignments[idx] = updated;
        }

        return updated;
      } catch (err) {
        this.error = err.message || "Failed to process coding action";
        throw err;
      } finally {
        this.isLoading = false;
      }
    },

    async batchAssign({
      assignmentIds,
      items,
      code,
      term,
      dictionaryType,
      dictionaryVersion,
      reason,
      action = "ACCEPT",
    }) {
      this.isLoading = true;
      this.error = null;
      try {
        const payload = {
          assignment_ids: assignmentIds,
          items,
          code,
          term,
          dictionary_type: dictionaryType,
          dictionary_version: dictionaryVersion,
          reason: reason || "Batch medical coding assignment",
          action,
        };

        const res = await apiClient.post(
          "/api/v1/execution/coding/assignments/batch-assign",
          payload,
          {
            changeReason: payload.reason,
          }
        );

        // Clear selections and refresh queue
        this.selectedAssignmentIds = [];
        await this.fetchAssignments();

        return res;
      } catch (err) {
        this.error = err.message || "Batch coding assignment failed";
        throw err;
      } finally {
        this.isLoading = false;
      }
    },

    async raiseQuery({ assignmentId, queryText, reason }) {
      this.isLoading = true;
      this.error = null;
      try {
        const payload = {
          query_text: queryText,
          reason: reason || "Coding discrepancy query escalation",
        };

        const res = await apiClient.post(
          `/api/v1/execution/coding/assignments/${assignmentId}/raise-query`,
          payload,
          {
            changeReason: payload.reason,
          }
        );

        // Update status locally
        const item = this.assignments.find((a) => a.id === assignmentId);
        if (item) {
          item.status = "QUERY_PENDING";
        }

        return res;
      } catch (err) {
        this.error = err.message || "Failed to raise discrepancy query";
        throw err;
      } finally {
        this.isLoading = false;
      }
    },

    async runImpactAnalysis({ dictionaryType, newVersion }) {
      this.impactAnalysis.isLoading = true;
      this.impactAnalysis.error = null;
      try {
        const payload = {
          dictionary_type: dictionaryType,
          new_version: newVersion,
        };

        const res = await apiClient.post(
          "/api/v1/execution/coding/impact-analysis",
          payload,
          {
            changeReason: `Dictionary upversioning impact analysis for ${dictionaryType} version ${newVersion}`,
          }
        );

        this.impactAnalysis.results = res.metrics || res;
        return res;
      } catch (err) {
        this.impactAnalysis.error =
          err.message || "Failed to run up-versioning impact analysis";
        throw err;
      } finally {
        this.impactAnalysis.isLoading = false;
      }
    },

    toggleSelect(assignmentId) {
      const idx = this.selectedAssignmentIds.indexOf(assignmentId);
      if (idx === -1) {
        this.selectedAssignmentIds.push(assignmentId);
      } else {
        this.selectedAssignmentIds.splice(idx, 1);
      }
    },

    selectAll(ids) {
      if (this.selectedAssignmentIds.length === ids.length) {
        this.selectedAssignmentIds = [];
      } else {
        this.selectedAssignmentIds = [...ids];
      }
    },

    clearSelection() {
      this.selectedAssignmentIds = [];
    },

    setViewMode(mode) {
      this.viewMode = mode;
    },

    toggleSelectCluster(clusterKey) {
      const idx = this.selectedClusterKeys.indexOf(clusterKey);
      if (idx === -1) {
        this.selectedClusterKeys.push(clusterKey);
      } else {
        this.selectedClusterKeys.splice(idx, 1);
      }
    },

    selectAllClusters(keys) {
      if (this.selectedClusterKeys.length === keys.length) {
        this.selectedClusterKeys = [];
      } else {
        this.selectedClusterKeys = [...keys];
      }
    },

    clearClusterSelection() {
      this.selectedClusterKeys = [];
    },

    async batchApproveClusters({ clusterKeys, reason } = {}) {
      const keysToProcess = clusterKeys || this.selectedClusterKeys;
      const targetClusters = this.verbatimClusters.filter((c) =>
        keysToProcess.includes(c.clusterKey)
      );

      const unblockedIds = [];
      for (const cluster of targetClusters) {
        unblockedIds.push(...cluster.unblocked_ids);
      }

      if (unblockedIds.length === 0) {
        throw new Error("No unblocked items found in selected clusters to approve.");
      }

      const res = await this.batchAssign({
        assignmentIds: unblockedIds,
        action: "ACCEPT",
        reason: reason || "Batch verbatim cluster approval",
      });

      this.selectedClusterKeys = [];
      return res;
    },

    async batchCodeClusters({ clusterKeys, code, term, dictionaryType, reason } = {}) {
      const keysToProcess = clusterKeys || this.selectedClusterKeys;
      const targetClusters = this.verbatimClusters.filter((c) =>
        keysToProcess.includes(c.clusterKey)
      );

      const unblockedIds = [];
      for (const cluster of targetClusters) {
        unblockedIds.push(...cluster.unblocked_ids);
      }

      if (unblockedIds.length === 0) {
        throw new Error("No unblocked items found in selected clusters to code.");
      }

      const res = await this.batchAssign({
        assignmentIds: unblockedIds,
        code,
        term,
        dictionaryType: dictionaryType || targetClusters[0]?.dictionary_type || "MEDDRA",
        action: "OVERRIDE",
        reason: reason || "Batch verbatim cluster code override",
      });

      this.selectedClusterKeys = [];
      return res;
    },

    openBrowser(assignment) {
      this.browserModal.isOpen = true;
      this.browserModal.assignment = assignment;
      this.browserModal.dictionaryType =
        assignment?.dictionary_type || "MEDDRA";
      this.browserModal.searchTerm = assignment?.verbatim_text || "";
      this.browserModal.version = assignment?.dictionary_version || "26.0";
      this.browserModal.targetLevel = "LLT";
      this.dictionarySearchResults = [];
      this.searchStatus = null;

      if (this.browserModal.searchTerm) {
        this.searchDictionary({
          term: this.browserModal.searchTerm,
          dictionaryType: this.browserModal.dictionaryType,
          version: this.browserModal.version,
          targetLevel: this.browserModal.targetLevel,
        });
      }
    },

    closeBrowser() {
      this.browserModal.isOpen = false;
      this.browserModal.assignment = null;
      this.dictionarySearchResults = [];
      this.searchStatus = null;
    },

    openImpactDrawer() {
      this.impactAnalysis.isOpen = true;
      this.impactAnalysis.error = null;
    },

    closeImpactDrawer() {
      this.impactAnalysis.isOpen = false;
    },
  },
});
