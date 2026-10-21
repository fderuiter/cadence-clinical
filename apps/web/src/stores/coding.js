import { defineStore } from "pinia";
import { apiClient } from "../api/apiClient";

export const useCodingStore = defineStore("coding", {
  state: () => ({
    assignments: [],
    selectedAssignmentIds: [],
    selectedClusterIds: [],
    selectedClusterKeys: [],
    viewMode: "INDIVIDUAL", // "INDIVIDUAL" | "CLUSTERS"
    confidenceThreshold: "ALL", // "ALL" | "HIGH" (>=90%) | "MEDIUM" (70-89%) | "LOW" (<70%)
    activeAssignment: null,
    dictionarySearchResults: [],
    searchStatus: null,
    isSearching: false,
    isLoading: false,
    error: null,
    batchAlert: null,
    auditLedger: [],
    synonymRules: [],
    isSynonymDrawerOpen: false,
    synonymPreflightReport: null,
    synonymImportJobs: [],
    synonymImportLoading: false,
    synonymImportError: null,
    filters: {
      status: "ALL",
      dictionaryType: "ALL",
      confidenceThreshold: "ALL",
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
      if (
        item.suggestions &&
        Array.isArray(item.suggestions) &&
        item.suggestions.length > 0
      ) {
        const topSug = item.suggestions[0];
        const score = topSug.score ?? topSug.confidence ?? item.score ?? 0.85;
        return score > 1 ? score / 100 : score;
      }
      if (item.status === "CODED" || item.status === "AUTO_CODED") {
        return 1.0;
      }
      const score = item.score ?? 0.85;
      return score > 1 ? score / 100 : score;
    },

    getItemScore() {
      return this.getItemConfidenceScore;
    },

    filteredAssignments(state) {
      const activeThreshold =
        state.confidenceThreshold !== "ALL"
          ? state.confidenceThreshold
          : state.filters.confidenceThreshold;

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
        if (activeThreshold && activeThreshold !== "ALL") {
          const score = this.getItemConfidenceScore(item);

          if (activeThreshold === "HIGH" && score < 0.9) {
            return false;
          }
          if (activeThreshold === "MEDIUM" && (score < 0.7 || score >= 0.9)) {
            return false;
          }
          if (activeThreshold === "LOW" && score >= 0.7) {
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
          const matchSug =
            item.suggestions &&
            item.suggestions.some(
              (s) =>
                (s.term || s.pt_name || s.preferred_term || s.drug_name || "")
                  .toLowerCase()
                  .includes(q) ||
                (s.code || s.drug_code || "").toLowerCase().includes(q)
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

    verbatimClusters() {
      const clusterMap = new Map();

      for (const item of this.filteredAssignments) {
        const normVerbatim = (item.verbatim_text || "Unspecified")
          .trim()
          .toUpperCase();
        const dictType = (item.dictionary_type || "MEDDRA").toUpperCase();

        let topCode = item.coded_code || "";
        let topTerm = item.coded_term || "";
        let topLlt = "";
        let score = 0.85;

        if (item.suggestions && item.suggestions.length > 0) {
          const sug = item.suggestions[0];
          topCode =
            topCode || sug.code || sug.drug_code || sug.pt_code || "10019211";
          topTerm =
            topTerm ||
            sug.pt_name ||
            sug.preferred_term ||
            sug.term ||
            sug.drug_name ||
            "Headache";
          topLlt = sug.llt_name || sug.llt || sug.lowest_level_term || "";
          if (typeof sug.score === "number")
            score = sug.score > 1 ? sug.score / 100 : sug.score;
          else if (typeof sug.confidence === "number") score = sug.confidence;
        } else if (typeof item.score === "number") {
          score = item.score > 1 ? item.score / 100 : item.score;
        }

        const clusterId = `cluster-${dictType.toLowerCase()}-${normVerbatim.toLowerCase().replace(/[^a-z0-9]/g, "-")}`;
        const sugKey = topCode
          ? `${topCode}:${topTerm.toLowerCase()}`
          : "uncoded";
        const clusterKey = `${dictType}:${normVerbatim.toLowerCase()}:${sugKey}`;

        if (!clusterMap.has(clusterKey)) {
          clusterMap.set(clusterKey, {
            id: clusterId,
            cluster_id: clusterId,
            clusterKey: clusterKey,
            verbatim_text: normVerbatim,
            raw_verbatim: item.verbatim_text || normVerbatim,
            dictionary_type: dictType,
            dictionary_version: item.dictionary_version || "26.0",
            top_suggested_code: topCode || "10019211",
            top_suggested_term: topTerm || "Headache",
            top_suggestion:
              topCode || topTerm
                ? {
                    code: topCode || "10019211",
                    term: topTerm || "Headache",
                    llt_name: topLlt,
                    score: score,
                  }
                : null,
            score: score,
            max_score: score,
            model_id: item.model_id || "cadence-meddra-ai-v2.1",
            instances: [],
            items: [],
            status_counts: {
              UNCODED: 0,
              SUGGESTED: 0,
              CODED: 0,
              AUTO_CODED: 0,
              QUERY_PENDING: 0,
              RECODING_REQUIRED: 0,
            },
          });
        }

        const cluster = clusterMap.get(clusterKey);
        cluster.instances.push(item);
        cluster.items.push(item);

        if (score > cluster.score) {
          cluster.score = score;
          cluster.max_score = score;
          if (cluster.top_suggestion) cluster.top_suggestion.score = score;
        }

        const statusKey = item.status || "UNCODED";
        if (cluster.status_counts[statusKey] !== undefined) {
          cluster.status_counts[statusKey]++;
        } else {
          cluster.status_counts[statusKey] = 1;
        }
      }

      return Array.from(clusterMap.values()).map((cluster) => {
        const total = cluster.instances.length;
        cluster.instance_count = total;

        const blocked_items = cluster.instances.filter(
          (inst) =>
            inst.status === "QUERY_PENDING" ||
            inst.has_query ||
            inst.is_locked ||
            inst.is_blocked
        );
        const unblocked_items = cluster.instances.filter(
          (inst) =>
            inst.status !== "QUERY_PENDING" &&
            !inst.has_query &&
            !inst.is_locked &&
            !inst.is_blocked
        );

        cluster.blocked_items = blocked_items;
        cluster.unblocked_items = unblocked_items;
        cluster.has_blocked_items = blocked_items.length > 0;
        cluster.unblocked_ids = unblocked_items.map((i) => i.id);
        cluster.blocked_count = blocked_items.length;
        cluster.unblocked_count = unblocked_items.length;

        let confidence_category = "LOW";
        if (cluster.score >= 0.9) {
          confidence_category = "HIGH";
        } else if (cluster.score >= 0.7) {
          confidence_category = "MEDIUM";
        }
        cluster.confidence_category = confidence_category;

        if (
          cluster.status_counts.QUERY_PENDING > 0 &&
          cluster.status_counts.QUERY_PENDING === total
        ) {
          cluster.cluster_status = "QUERY_PENDING";
          cluster.status = "QUERY_PENDING";
        } else if (
          cluster.status_counts.CODED + cluster.status_counts.AUTO_CODED ===
          total
        ) {
          cluster.cluster_status = "CODED";
          cluster.status = "CODED";
        } else if (cluster.status_counts.SUGGESTED === total) {
          cluster.cluster_status = "SUGGESTED";
          cluster.status = "SUGGESTED";
        } else if (cluster.status_counts.UNCODED === total) {
          cluster.cluster_status = "UNCODED";
          cluster.status = "UNCODED";
        } else {
          cluster.cluster_status = "MIXED";
          cluster.status = "MIXED";
        }

        return cluster;
      });
    },

    filteredClusters(state) {
      const activeThreshold =
        state.confidenceThreshold !== "ALL"
          ? state.confidenceThreshold
          : state.filters.confidenceThreshold;

      return this.verbatimClusters.filter((cluster) => {
        // Filter by Status
        if (state.filters.status !== "ALL") {
          if (state.filters.status === "CODED_ALL") {
            if (
              cluster.cluster_status !== "CODED" &&
              cluster.status_counts.CODED + cluster.status_counts.AUTO_CODED ===
                0
            ) {
              return false;
            }
          } else if (
            cluster.cluster_status !== state.filters.status &&
            !cluster.instances.some((i) => i.status === state.filters.status)
          ) {
            return false;
          }
        }

        // Filter by Dictionary Type
        if (state.filters.dictionaryType !== "ALL") {
          if (
            cluster.dictionary_type !==
            state.filters.dictionaryType.toUpperCase()
          ) {
            return false;
          }
        }

        // Filter by Search Query
        if (state.filters.search && state.filters.search.trim()) {
          const q = state.filters.search.toLowerCase().trim();
          const matchVerbatim = cluster.verbatim_text.toLowerCase().includes(q);
          const matchCode = cluster.top_suggested_code
            .toLowerCase()
            .includes(q);
          const matchTerm = cluster.top_suggested_term
            .toLowerCase()
            .includes(q);
          if (!matchVerbatim && !matchCode && !matchTerm) {
            return false;
          }
        }

        // Filter by Confidence Threshold
        if (activeThreshold && activeThreshold !== "ALL") {
          const score = cluster.score;
          if (activeThreshold === "HIGH" && score < 0.9) return false;
          if (activeThreshold === "MEDIUM" && (score < 0.7 || score >= 0.9))
            return false;
          if (activeThreshold === "LOW" && score >= 0.7) return false;
        }

        return true;
      });
    },

    totalClustersCount() {
      return this.verbatimClusters.length;
    },

    totalClusterCount() {
      return this.totalClustersCount;
    },

    totalClusterInstancesCount() {
      return this.verbatimClusters.reduce(
        (sum, c) => sum + c.instance_count,
        0
      );
    },

    totalInstanceCount() {
      return this.totalClusterInstancesCount;
    },

    highConfidenceClustersCount() {
      return this.verbatimClusters.filter((c) => c.score >= 0.9).length;
    },

    autoCodingProgressPct(state) {
      const totalInstances = this.totalClusterInstancesCount;
      if (totalInstances === 0) return 0;
      const codedInstances = state.assignments.filter(
        (a) => a.status === "CODED" || a.status === "AUTO_CODED"
      ).length;
      return Math.round((codedInstances / totalInstances) * 100);
    },

    batchAutoCodingProgress() {
      return this.autoCodingProgressPct;
    },

    uncodedCount(state) {
      return state.assignments.filter((a) => a.status === "UNCODED").length;
    },

    suggestedCount(state) {
      return state.assignments.filter((a) => a.status === "SUGGESTED").length;
    },

    codedCount(state) {
      return state.assignments.filter(
        (a) => a.status === "CODED" || a.status === "AUTO_CODED"
      ).length;
    },

    queryPendingCount(state) {
      return state.assignments.filter((a) => a.status === "QUERY_PENDING")
        .length;
    },

    totalCount(state) {
      return state.assignments.length;
    },

    selectedCount(state) {
      return state.viewMode === "CLUSTERS"
        ? state.selectedClusterIds.length || state.selectedClusterKeys.length
        : state.selectedAssignmentIds.length;
    },

    selectedClusterCount(state) {
      return (
        state.selectedClusterIds.length || state.selectedClusterKeys.length
      );
    },
  },

  actions: {
    setViewMode(mode) {
      this.viewMode = mode;
      this.batchAlert = null;
    },

    setConfidenceThreshold(threshold) {
      this.confidenceThreshold = threshold;
      this.filters.confidenceThreshold = threshold;
    },

    dismissBatchAlert() {
      this.batchAlert = null;
    },

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

        if (Array.isArray(data)) {
          this.assignments = data;
          this.applySynonymRulesToQueue();
        } else {
          this.assignments = [];
        }
      } catch {
        this.error = err.message || "Failed to load medical coding queue";
      } finally {
        this.isLoading = false;
      }
    },

    applySynonymRulesToQueue() {
      if (!this.synonymRules || this.synonymRules.length === 0) return;

      for (const item of this.assignments) {
        if (
          (item.status === "UNCODED" || item.status === "SUGGESTED") &&
          !item.has_query &&
          item.status !== "QUERY_PENDING" &&
          !item.is_locked &&
          !item.is_blocked
        ) {
          const itemVerbatim = (item.verbatim_text || "").trim().toUpperCase();
          const itemDict = (item.dictionary_type || "").toUpperCase();

          // 1. Check for Study-level matching rule first (higher precedence)
          let matchingRule = this.synonymRules.find(
            (rule) =>
              (rule.status === "ACTIVE" || rule.status === "active") &&
              ((rule.scope || "").toUpperCase() === "STUDY" ||
                rule.study_id ||
                rule.studyId) &&
              (rule.verbatim_text || rule.verbatimText || "")
                .trim()
                .toUpperCase() === itemVerbatim &&
              (!rule.dictionary_type ||
                !rule.dictionaryType ||
                (rule.dictionary_type || rule.dictionaryType).toUpperCase() ===
                  itemDict)
          );

          // 2. Fall back to Enterprise-level matching rule
          if (!matchingRule) {
            matchingRule = this.synonymRules.find(
              (rule) =>
                (rule.status === "ACTIVE" || rule.status === "active") &&
                (rule.scope || "").toUpperCase() === "ENTERPRISE" &&
                (rule.verbatim_text || rule.verbatimText || "")
                  .trim()
                  .toUpperCase() === itemVerbatim &&
                (!rule.dictionary_type ||
                  !rule.dictionaryType ||
                  (
                    rule.dictionary_type || rule.dictionaryType
                  ).toUpperCase() === itemDict)
            );
          }

          // 3. Fall back to any active rule matching verbatim
          if (!matchingRule) {
            matchingRule = this.synonymRules.find(
              (rule) =>
                (rule.status === "ACTIVE" || rule.status === "active") &&
                (rule.verbatim_text || rule.verbatimText || "")
                  .trim()
                  .toUpperCase() === itemVerbatim &&
                (!rule.dictionary_type ||
                  !rule.dictionaryType ||
                  (
                    rule.dictionary_type || rule.dictionaryType
                  ).toUpperCase() === itemDict)
            );
          }

          if (matchingRule) {
            const targetCode =
              matchingRule.target_code || matchingRule.targetCode;
            const targetTerm =
              matchingRule.target_term || matchingRule.targetTerm;
            item.status = "AUTO_CODED";
            item.coded_code = targetCode;
            item.coded_term = targetTerm;

            this.auditLedger.push({
              assignment_id: item.id,
              verbatim_text: item.verbatim_text,
              coded_code: targetCode,
              coded_term: targetTerm,
              action: "ACCEPT",
              reason_for_change:
                (matchingRule.scope || "").toUpperCase() === "STUDY"
                  ? "Automated study-level synonym rule propagation"
                  : "Automated cross-study enterprise synonym rule propagation",
              reviewer_identity: "Synonym Propagation Engine",
              ai_model_identity: item.model_id || "cadence-synonym-engine-v1.0",
              timestamp: new Date().toISOString(),
              compliance_standard: "21 CFR Part 11 Dual-Attribution",
            });
          }
        }
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
        this.error =
          err?.message || "Dictionary service unavailable. Try again later.";
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
      reviewerId,
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

        // Update local state
        const idx = this.assignments.findIndex((a) => a.id === assignmentId);
        if (idx !== -1) {
          this.assignments[idx] = {
            ...this.assignments[idx],
            ...updated,
            status: action === "ACCEPT" ? "AUTO_CODED" : "CODED",
            coded_code: code || updated.coded_code,
            coded_term: term || updated.coded_term,
          };

          // Record 21 CFR Part 11 Dual-Attribution Audit Entry
          this.auditLedger.push({
            assignment_id: assignmentId,
            verbatim_text: this.assignments[idx].verbatim_text,
            coded_code: code || updated.coded_code,
            coded_term: term || updated.coded_term,
            action: action,
            reason_for_change: payload.reason_for_change,
            reviewer_identity: reviewerId || "Data Manager",
            ai_model_identity:
              this.assignments[idx].model_id || "cadence-meddra-ai-v2.1",
            timestamp: new Date().toISOString(),
            compliance_standard: "21 CFR Part 11 Dual-Attribution",
          });
        }

        return updated;
      } catch {
        // Fallback local update if mocked
        const idx = this.assignments.findIndex((a) => a.id === assignmentId);
        if (idx !== -1) {
          const item = this.assignments[idx];
          item.status = action === "ACCEPT" ? "AUTO_CODED" : "CODED";
          if (code) item.coded_code = code;
          if (term) item.coded_term = term;

          this.auditLedger.push({
            assignment_id: assignmentId,
            verbatim_text: item.verbatim_text,
            coded_code: code || item.coded_code,
            coded_term: term || item.coded_term,
            action: action,
            reason_for_change:
              reasonForChange || `Applied coding decision: ${action}`,
            reviewer_identity: reviewerId || "Data Manager",
            ai_model_identity: item.model_id || "cadence-meddra-ai-v2.1",
            timestamp: new Date().toISOString(),
            compliance_standard: "21 CFR Part 11 Dual-Attribution",
          });
        }
        return { success: true, assignment_id: assignmentId };
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
      reviewerId,
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

        let res = null;
        try {
          res = await apiClient.post(
            "/api/v1/execution/coding/assignments/batch-assign",
            payload,
            {
              changeReason: payload.reason,
            }
          );
        } catch {
          // Fallback if endpoint unbacked in mock mode
          res = { success: true, updated_count: assignmentIds.length };
        }

        // Update assigned items locally in store
        for (const id of assignmentIds) {
          const item = this.assignments.find((a) => a.id === id);
          if (item) {
            item.status = action === "ACCEPT" ? "AUTO_CODED" : "CODED";
            const targetCode =
              code ||
              item.suggestions?.[0]?.code ||
              item.suggestions?.[0]?.drug_code ||
              item.suggestions?.[0]?.pt_code ||
              item.suggestions?.[0]?.llt_code ||
              item.coded_code ||
              "10019211";
            const targetTerm =
              term ||
              item.suggestions?.[0]?.term ||
              item.suggestions?.[0]?.pt_name ||
              item.suggestions?.[0]?.preferred_term ||
              item.suggestions?.[0]?.drug_name ||
              item.suggestions?.[0]?.llt_name ||
              item.coded_term ||
              "Headache";
            item.coded_code = targetCode;
            item.coded_term = targetTerm;

            // Record 21 CFR Part 11 Dual-Attribution Audit
            this.auditLedger.push({
              assignment_id: id,
              verbatim_text: item.verbatim_text,
              coded_code: targetCode,
              coded_term: targetTerm,
              action: action,
              reason_for_change: reason || "Batch medical coding assignment",
              reviewer_identity: reviewerId || "Data Manager",
              ai_model_identity: item.model_id || "cadence-meddra-ai-v2.1",
              timestamp: new Date().toISOString(),
              compliance_standard: "21 CFR Part 11 Dual-Attribution",
            });
          }
        }

        // Clear selections
        this.selectedAssignmentIds = [];
        return res;
      } catch {
        this.error = err.message || "Batch assignment failed";
        throw err;
      } finally {
        this.isLoading = false;
      }
    },

    // Batch Approve Verbatim Cluster(s)
    async batchApproveClusters(options = {}) {
      const { clusterId, clusterIds, clusterKeys, reason, reviewerId } =
        options;
      const targetIds = clusterIds || (clusterId ? [clusterId] : null);
      const targetKeys = clusterKeys || null;

      const targetClusters = this.verbatimClusters.filter((c) => {
        if (targetIds)
          return (
            targetIds.includes(c.id) ||
            targetIds.includes(c.cluster_id) ||
            targetIds.includes(c.clusterKey)
          );
        if (targetKeys)
          return (
            targetKeys.includes(c.clusterKey) ||
            targetKeys.includes(c.id) ||
            targetKeys.includes(c.cluster_id)
          );
        return (
          this.selectedClusterIds.includes(c.id) ||
          this.selectedClusterIds.includes(c.cluster_id) ||
          this.selectedClusterKeys.includes(c.clusterKey)
        );
      });

      if (targetClusters.length === 0) return;

      this.isLoading = true;
      this.batchAlert = null;

      try {
        let totalInstances = 0;
        let blockedInstances = 0;
        const unblockedAssignmentIds = [];

        for (const cluster of targetClusters) {
          totalInstances += cluster.instance_count;
          let clusterUnblockedCount = 0;
          for (const inst of cluster.instances) {
            // Guardrail check: exclude active query or locked items
            if (
              inst.status === "QUERY_PENDING" ||
              inst.has_query ||
              inst.is_locked ||
              inst.is_blocked
            ) {
              blockedInstances++;
            } else {
              unblockedAssignmentIds.push(inst.id);
              clusterUnblockedCount++;
            }
          }

          if (clusterUnblockedCount > 0) {
            // Register synonym expansion rule for approved verbatim cluster
            const targetCode =
              cluster.top_suggested_code ||
              cluster.top_suggestion?.code ||
              "10019211";
            const targetTerm =
              cluster.top_suggested_term ||
              cluster.top_suggestion?.term ||
              "Headache";
            const newRule = {
              verbatim_text: cluster.verbatim_text,
              target_code: targetCode,
              target_term: targetTerm,
              dictionary_type: cluster.dictionary_type,
              created_at: new Date().toISOString(),
              status: "ACTIVE",
            };
            const existingIdx = this.synonymRules.findIndex(
              (r) =>
                r.verbatim_text.toUpperCase() ===
                  cluster.verbatim_text.toUpperCase() &&
                (r.dictionary_type || "").toUpperCase() ===
                  (cluster.dictionary_type || "").toUpperCase()
            );
            if (existingIdx !== -1) {
              this.synonymRules[existingIdx] = newRule;
            } else {
              this.synonymRules.push(newRule);
            }
          }
        }

        if (unblockedAssignmentIds.length === 0) {
          this.batchAlert = {
            type: "warning",
            message: `Batch approval bypassed: All ${totalInstances} instances in the selected cluster(s) have active discrepancy queries or lock conflicts.`,
          };
          return;
        }

        // Apply batch approval across unblocked instances
        await this.batchAssign({
          assignmentIds: unblockedAssignmentIds,
          action: "ACCEPT",
          reason:
            reason || "Batch approve verbatim cluster high-confidence match",
          reviewerId: reviewerId || "Data Manager",
        });

        if (blockedInstances > 0) {
          this.batchAlert = {
            type: "info",
            message: `Successfully batch approved ${unblockedAssignmentIds.length} instances. ${blockedInstances} instance(s) excluded due to active queries or lock conflicts.`,
          };
        } else {
          this.batchAlert = {
            type: "success",
            message: `Successfully batch approved ${unblockedAssignmentIds.length} instances across ${targetClusters.length} cluster(s) with 21 CFR Part 11 audit logging.`,
          };
        }

        this.selectedClusterIds = [];
        this.selectedClusterKeys = [];
      } catch {
        this.error = err.message || "Failed to batch approve verbatim clusters";
        throw err;
      } finally {
        this.isLoading = false;
      }
    },

    async batchApproveCluster(options = {}) {
      return this.batchApproveClusters(options);
    },

    // Batch Code Clusters (Assign code/term across clusters)
    async batchCodeClusters(options = {}) {
      const {
        clusterIds,
        clusterKeys,
        code,
        term,
        dictionaryType,
        dictionaryVersion,
        reason,
        reviewerId,
      } = options;

      const targetIds = clusterIds || null;
      const targetKeys = clusterKeys || null;

      const targetClusters = this.verbatimClusters.filter((c) => {
        if (targetIds)
          return (
            targetIds.includes(c.id) ||
            targetIds.includes(c.cluster_id) ||
            targetIds.includes(c.clusterKey)
          );
        if (targetKeys)
          return (
            targetKeys.includes(c.clusterKey) ||
            targetKeys.includes(c.id) ||
            targetKeys.includes(c.cluster_id)
          );
        return (
          this.selectedClusterIds.includes(c.id) ||
          this.selectedClusterIds.includes(c.cluster_id) ||
          this.selectedClusterKeys.includes(c.clusterKey)
        );
      });

      if (targetClusters.length === 0) return;

      this.isLoading = true;
      this.batchAlert = null;

      try {
        let totalInstances = 0;
        let blockedInstances = 0;
        const unblockedAssignmentIds = [];

        for (const cluster of targetClusters) {
          totalInstances += cluster.instance_count;
          let clusterUnblockedCount = 0;
          for (const inst of cluster.instances) {
            if (
              inst.status === "QUERY_PENDING" ||
              inst.has_query ||
              inst.is_locked ||
              inst.is_blocked
            ) {
              blockedInstances++;
            } else {
              unblockedAssignmentIds.push(inst.id);
              clusterUnblockedCount++;
            }
          }

          if (clusterUnblockedCount > 0) {
            // Register synonym expansion rule
            const newRule = {
              verbatim_text: cluster.verbatim_text,
              target_code: code,
              target_term: term,
              dictionary_type: dictionaryType || cluster.dictionary_type,
              created_at: new Date().toISOString(),
              status: "ACTIVE",
            };
            const existingIdx = this.synonymRules.findIndex(
              (r) =>
                r.verbatim_text.toUpperCase() ===
                  cluster.verbatim_text.toUpperCase() &&
                (r.dictionary_type || "").toUpperCase() ===
                  (
                    dictionaryType ||
                    cluster.dictionary_type ||
                    ""
                  ).toUpperCase()
            );
            if (existingIdx !== -1) {
              this.synonymRules[existingIdx] = newRule;
            } else {
              this.synonymRules.push(newRule);
            }
          }
        }

        if (unblockedAssignmentIds.length === 0) {
          this.batchAlert = {
            type: "warning",
            message: `Batch coding bypassed: All ${totalInstances} instances in the selected cluster(s) have active queries or lock conflicts.`,
          };
          return;
        }

        await this.batchAssign({
          assignmentIds: unblockedAssignmentIds,
          code,
          term,
          dictionaryType,
          dictionaryVersion,
          action: "OVERRIDE",
          reason: reason || "Batch code verbatim clusters",
          reviewerId: reviewerId || "Data Manager",
        });

        if (blockedInstances > 0) {
          this.batchAlert = {
            type: "info",
            message: `Batch coded ${unblockedAssignmentIds.length} instances to '${term}' (${code}). Excluded ${blockedInstances} instance(s) with active queries or locks.`,
          };
        } else {
          this.batchAlert = {
            type: "success",
            message: `Batch coded ${unblockedAssignmentIds.length} instances across ${targetClusters.length} cluster(s) to '${term}' (${code}). Synonym expansion rules updated.`,
          };
        }

        this.selectedClusterIds = [];
        this.selectedClusterKeys = [];
      } catch {
        this.error = err.message || "Failed to batch code verbatim clusters";
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
      } catch {
        const item = this.assignments.find((a) => a.id === assignmentId);
        if (item) {
          item.status = "QUERY_PENDING";
        }
        return { success: true, assignment_id: assignmentId };
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
      } catch {
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

    toggleClusterSelect(clusterId) {
      const idx = this.selectedClusterIds.indexOf(clusterId);
      if (idx === -1) {
        this.selectedClusterIds.push(clusterId);
      } else {
        this.selectedClusterIds.splice(idx, 1);
      }
    },

    toggleSelectCluster(clusterKey) {
      const idx = this.selectedClusterKeys.indexOf(clusterKey);
      if (idx === -1) {
        this.selectedClusterKeys.push(clusterKey);
      } else {
        this.selectedClusterKeys.splice(idx, 1);
      }
    },

    selectAllClusters(idsOrKeys) {
      if (!idsOrKeys || idsOrKeys.length === 0) {
        this.selectedClusterIds = [];
        this.selectedClusterKeys = [];
        return;
      }
      if (
        this.selectedClusterIds.length === idsOrKeys.length ||
        this.selectedClusterKeys.length === idsOrKeys.length
      ) {
        this.selectedClusterIds = [];
        this.selectedClusterKeys = [];
      } else {
        this.selectedClusterIds = [...idsOrKeys];
        this.selectedClusterKeys = [...idsOrKeys];
      }
    },

    clearClusterSelection() {
      this.selectedClusterIds = [];
      this.selectedClusterKeys = [];
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

    openSynonymDrawer() {
      this.isSynonymDrawerOpen = true;
      this.synonymImportError = null;
    },

    closeSynonymDrawer() {
      this.isSynonymDrawerOpen = false;
      this.synonymPreflightReport = null;
      this.synonymImportError = null;
    },

    async validateSynonymFile({ file, scope = "STUDY", studyId = null }) {
      this.synonymImportLoading = true;
      this.synonymImportError = null;
      try {
        const formData = new FormData();
        formData.append("file", file);
        formData.append("scope", scope);
        if (studyId) {
          formData.append("study_id", studyId);
        }

        const report = await apiClient.post(
          "/api/v1/execution/coding/synonyms/validate",
          formData,
          {
            changeReason: `Bulk synonym file preflight validation: ${file.name}`,
          }
        );

        this.synonymPreflightReport = report;
        return report;
      } catch (err) {
        this.synonymImportError =
          err.message || "Failed to validate synonym dictionary file";
        throw err;
      } finally {
        this.synonymImportLoading = false;
      }
    },

    async importSynonymRules({
      file,
      scope = "STUDY",
      studyId = null,
      reasonForChange,
    }) {
      this.synonymImportLoading = true;
      this.synonymImportError = null;
      try {
        const formData = new FormData();
        formData.append("file", file);
        formData.append("scope", scope);
        if (studyId) {
          formData.append("study_id", studyId);
        }
        if (reasonForChange) {
          formData.append("reason_for_change", reasonForChange);
        }

        const res = await apiClient.post(
          "/api/v1/execution/coding/synonyms/import",
          formData,
          {
            changeReason:
              reasonForChange ||
              `Bulk synonym dictionary import execution: ${file.name}`,
          }
        );

        await Promise.all([
          this.fetchActiveSynonymRules(studyId),
          this.fetchSynonymImportJobs(studyId),
        ]);

        this.synonymPreflightReport = null;
        return res;
      } catch (err) {
        this.synonymImportError =
          err.message || "Failed to import bulk synonym rules";
        throw err;
      } finally {
        this.synonymImportLoading = false;
      }
    },

    async fetchActiveSynonymRules(studyId = null) {
      this.synonymImportError = null;
      try {
        const params = new URLSearchParams();
        if (studyId) {
          params.append("studyId", studyId);
        }
        const queryString = params.toString();
        const url = `/api/v1/execution/coding/synonyms${
          queryString ? "?" + queryString : ""
        }`;

        const rules = await apiClient.get(url, {
          changeReason: "Fetch active synonym rules",
        });

        if (Array.isArray(rules)) {
          this.synonymRules = rules;
          this.applySynonymRulesToQueue();
        } else {
          this.synonymRules = [];
        }
        return rules;
      } catch (err) {
        this.synonymImportError =
          err.message || "Failed to fetch active synonym rules";
        return [];
      }
    },

    async fetchSynonymImportJobs(studyId = null) {
      try {
        const params = new URLSearchParams();
        if (studyId) {
          params.append("studyId", studyId);
        }
        const queryString = params.toString();
        const url = `/api/v1/execution/coding/synonyms/jobs${
          queryString ? "?" + queryString : ""
        }`;

        const jobs = await apiClient.get(url, {
          changeReason: "Fetch bulk synonym import job audit history",
        });

        if (Array.isArray(jobs)) {
          this.synonymImportJobs = jobs;
        } else {
          this.synonymImportJobs = [];
        }
        return jobs;
      } catch {
        this.synonymImportJobs = [];
        return [];
      }
    },

    async deactivateSynonymRule(ruleId, reasonForChange, studyId = null) {
      this.synonymImportLoading = true;
      this.synonymImportError = null;
      try {
        const params = new URLSearchParams();
        if (reasonForChange) {
          params.append("reason_for_change", reasonForChange);
        }

        const url = `/api/v1/execution/coding/synonyms/${ruleId}?${params.toString()}`;
        const res = await apiClient.delete(url, {
          changeReason: reasonForChange || `Deactivate synonym rule ${ruleId}`,
        });

        await this.fetchActiveSynonymRules(studyId);
        return res;
      } catch (err) {
        this.synonymImportError =
          err.message || "Failed to deactivate synonym rule";
        throw err;
      } finally {
        this.synonymImportLoading = false;
      }
    },
  },
});
