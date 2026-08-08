import { defineStore } from "pinia";
import { apiClient } from "../api/apiClient";

export const useAdminStore = defineStore("admin", {
  state: () => ({
    sites: [],
    personnel: [],
    assignments: {}, // maps personnel_id to assignment list
    organizations: [],
    loading: false,
    error: null,
  }),
  actions: {
    async fetchOrganizations() {
      this.loading = true;
      this.error = null;
      try {
        const response = await apiClient.get("/api/v1/org/organizations");
        this.organizations = response;
      } catch (err) {
        this.error = err.message || "Failed to fetch organizations";
      } finally {
        this.loading = false;
      }
    },
    async createOrganization(payload, changeReason) {
      this.loading = true;
      this.error = null;
      try {
        const response = await apiClient.post("/api/v1/org/organizations", {
          ...payload,
          reason_for_change: changeReason,
        }, {
          changeReason
        });
        await this.fetchOrganizations();
        return response;
      } catch (err) {
        this.error = err.message || "Failed to create organization";
        throw err;
      } finally {
        this.loading = false;
      }
    },
    async fetchSites() {
      this.loading = true;
      this.error = null;
      try {
        const response = await apiClient.get("/api/v1/org/sites");
        this.sites = response;
      } catch (err) {
        this.error = err.message || "Failed to fetch sites";
      } finally {
        this.loading = false;
      }
    },
    async createSite(payload, changeReason) {
      this.loading = true;
      this.error = null;
      try {
        const response = await apiClient.post("/api/v1/org/sites", {
          ...payload,
          reason_for_change: changeReason,
        }, {
          changeReason
        });
        await this.fetchSites();
        return response;
      } catch (err) {
        this.error = err.message || "Failed to create site";
        throw err;
      } finally {
        this.loading = false;
      }
    },
    async updateSite(id, payload, changeReason) {
      this.loading = true;
      this.error = null;
      try {
        const response = await apiClient.put(`/api/v1/org/sites/${id}`, {
          ...payload,
          reason_for_change: changeReason,
        }, {
          changeReason
        });
        await this.fetchSites();
        return response;
      } catch (err) {
        this.error = err.message || "Failed to update site";
        throw err;
      } finally {
        this.loading = false;
      }
    },
    async fetchPersonnel() {
      this.loading = true;
      this.error = null;
      try {
        const response = await apiClient.get("/api/v1/org/personnel");
        this.personnel = response;
      } catch (err) {
        this.error = err.message || "Failed to fetch personnel";
      } finally {
        this.loading = false;
      }
    },
    async createPersonnel(payload, changeReason) {
      this.loading = true;
      this.error = null;
      try {
        const response = await apiClient.post("/api/v1/org/personnel", {
          ...payload,
          reason_for_change: changeReason,
        }, {
          changeReason
        });
        await this.fetchPersonnel();
        return response;
      } catch (err) {
        this.error = err.message || "Failed to create personnel";
        throw err;
      } finally {
        this.loading = false;
      }
    },
    async updatePersonnel(id, payload, changeReason) {
      this.loading = true;
      this.error = null;
      try {
        const response = await apiClient.put(`/api/v1/org/personnel/${id}`, {
          ...payload,
          reason_for_change: changeReason,
        }, {
          changeReason
        });
        await this.fetchPersonnel();
        return response;
      } catch (err) {
        this.error = err.message || "Failed to update personnel";
        throw err;
      } finally {
        this.loading = false;
      }
    },
    async fetchAssignments(personnelId) {
      this.loading = true;
      this.error = null;
      try {
        const response = await apiClient.get(`/api/v1/org/personnel/${personnelId}/assignments`);
        this.assignments[personnelId] = response;
      } catch (err) {
        this.error = err.message || `Failed to fetch assignments for personnel ${personnelId}`;
      } finally {
        this.loading = false;
      }
    },
    async createAssignment(personnelId, payload, changeReason) {
      this.loading = true;
      this.error = null;
      try {
        const response = await apiClient.post(`/api/v1/org/personnel/${personnelId}/assignments`, {
          ...payload,
          reason_for_change: changeReason,
        }, {
          changeReason
        });
        await this.fetchAssignments(personnelId);
        return response;
      } catch (err) {
        this.error = err.message || "Failed to assign personnel to site";
        throw err;
      } finally {
        this.loading = false;
      }
    }
  }
});
