import { defineStore } from "pinia";
import { apiClient } from "../api/apiClient";

export const useCodingStore = defineStore("coding", {
  state: () => ({
    assignments: [],
    selectedAssignmentIds: [],
    activeAssignment: null,
    dictionarySearchResults: [],
    searchStatus: null,
    isSearching: false,
    isLoading: false,
    error: null,
    filters: {
      status: "ALL",
      dictionaryType: "ALL",
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

        // Filter by search query
        if (state.filters.search && state.filters.search.trim()) {
          const q = state.filters.search.toLowerCase().trim();
          const matchVerbatim = (item.verbatim_text || "").toLowerCase().includes(q);
          const matchCode = (item.coded_code || "").toLowerCase().includes(q);
          const matchTerm = (item.coded_term || "").toLowerCase().includes(q);
          const matchField = (item.source_field || "").toLowerCase().includes(q);
          const matchObs = (item.observation_id || "").toLowerCase().includes(q);
          if (!matchVerbatim && !matchCode && !matchTerm && !matchField && !matchObs) {
            return false;
          }
        }

        return true;
      });
    },

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
      return state.assignments.filter((a) => a.status === "QUERY_PENDING").length;
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

    openBrowser(assignment) {
      this.browserModal.isOpen = true;
      this.browserModal.assignment = assignment;
      this.browserModal.dictionaryType = assignment?.dictionary_type || "MEDDRA";
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
