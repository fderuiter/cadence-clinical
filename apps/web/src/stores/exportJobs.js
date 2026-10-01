import { defineStore } from "pinia";
import { useToastStore } from "./toasts.js";
import { getBaseUrl } from "../api/client.js";
import { getBearerToken } from "../api/authorization.js";

export const useExportJobsStore = defineStore("exportJobs", {
  state: () => ({
    jobs: [],
    activeJobId: null,
    pollingTimers: {},
  }),

  getters: {
    activeJob: (state) => state.jobs.find((j) => j.jobId === state.activeJobId) || null,
    completedJobs: (state) => state.jobs.filter((j) => j.status === "Completed"),
    pendingJobs: (state) => state.jobs.filter((j) => j.status === "Queued" || j.status === "Processing"),
  },

  actions: {
    async submitExportJob(exportConfig) {
      const toastStore = useToastStore();
      const baseUrl = getBaseUrl();
      const token = await getBearerToken();

      const payload = {
        studyId: exportConfig.studyId || "STUDY-001",
        format: exportConfig.format || "dataset_json",
        domains: exportConfig.domains || [],
        datasets: exportConfig.datasets || [],
        sites: exportConfig.sites || [],
        cohorts: exportConfig.cohorts || [],
        privacyProfile: exportConfig.privacyProfile || "SAFE_HARBOR",
        hmacSalt: exportConfig.hmacSalt || "",
        recordLockFilter: exportConfig.recordLockFilter || "all",
        includeAuditTrail: exportConfig.includeAuditTrail ?? true,
        includeUnmapped: exportConfig.includeUnmapped ?? true,
      };

      try {
        const response = await fetch(`${baseUrl}/api/v1/execution/exports`, {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            ...(token ? { Authorization: `Bearer ${token}` } : {}),
          },
          body: JSON.stringify(payload),
        });

        if (!response.ok && response.status !== 202) {
          const errData = await response.json().catch(() => ({}));
          throw new Error(errData.detail || errData.title || `Export request failed with status ${response.status}`);
        }

        const job = await response.json();
        this.upsertJob(job);
        this.activeJobId = job.jobId;

        // Start active background polling with exponential backoff
        this.startPolling(job.jobId);

        return job;
      } catch (err) {
        toastStore.apiError("Failed to submit background export job", err);
        throw err;
      }
    },

    upsertJob(job) {
      const idx = this.jobs.findIndex((j) => j.jobId === job.jobId);
      if (idx >= 0) {
        this.jobs[idx] = { ...this.jobs[idx], ...job };
      } else {
        this.jobs.unshift(job);
      }
    },

    async fetchJobStatus(jobId) {
      const baseUrl = getBaseUrl();
      const token = await getBearerToken();

      try {
        const res = await fetch(`${baseUrl}/api/v1/execution/exports/${jobId}`, {
          headers: token ? { Authorization: `Bearer ${token}` } : {},
        });
        if (!res.ok) return null;
        const job = await res.json();
        this.upsertJob(job);
        return job;
      } catch (err) {
        console.warn(`Failed to poll status for export job ${jobId}:`, err);
        return null;
      }
    },

    async fetchJobs(studyId = null) {
      const baseUrl = getBaseUrl();
      const token = await getBearerToken();
      const url = studyId
        ? `${baseUrl}/api/v1/execution/exports?studyId=${encodeURIComponent(studyId)}`
        : `${baseUrl}/api/v1/execution/exports`;

      try {
        const res = await fetch(url, {
          headers: token ? { Authorization: `Bearer ${token}` } : {},
        });
        if (res.ok) {
          const list = await res.json();
          if (Array.isArray(list)) {
            list.forEach((j) => this.upsertJob(j));
          }
        }
      } catch (err) {
        console.warn("Failed to fetch export jobs:", err);
      }
    },

    startPolling(jobId) {
      if (this.pollingTimers[jobId]) {
        clearTimeout(this.pollingTimers[jobId]);
      }

      let delay = 2000; // Start at 2s
      const maxDelay = 10000; // Cap at 10s

      const poll = async () => {
        const job = await this.fetchJobStatus(jobId);

        if (!job) {
          // Retry
          this.pollingTimers[jobId] = setTimeout(poll, delay);
          delay = Math.min(delay * 2, maxDelay);
          return;
        }

        if (job.status === "Queued" || job.status === "Processing") {
          this.pollingTimers[jobId] = setTimeout(poll, delay);
          delay = Math.min(delay * 2, maxDelay); // Exponential backoff
        } else {
          // Finished polling
          delete this.pollingTimers[jobId];

          if (job.status === "Completed") {
            const downloadUrl =
              job.downloadUrl || `${getBaseUrl()}/api/v1/execution/exports/${jobId}/download`;

            useToastStore().push({
              tone: "success",
              title: "Export Package Ready",
              message: `Dataset export package for study ${job.studyId} is ready.`,
              action: {
                label: "📥 Download Package",
                onClick: () => this.downloadArtifact(jobId, downloadUrl),
              },
            });
          } else if (job.status === "Failed") {
            useToastStore().push({
              tone: "error",
              title: "Export Job Failed",
              message: `Export compilation failed: ${job.errorDetails || "Internal serialization error"}. Diagnostic guidance: Check execution audit ledger.`,
            });
          }
        }
      };

      poll();
    },

    async downloadArtifact(jobId, customUrl = null) {
      const baseUrl = getBaseUrl();
      const url = customUrl
        ? (customUrl.startsWith("http") ? customUrl : `${baseUrl}${customUrl}`)
        : `${baseUrl}/api/v1/execution/exports/${jobId}/download`;

      const token = await getBearerToken();

      try {
        const res = await fetch(url, {
          headers: token ? { Authorization: `Bearer ${token}` } : {},
        });

        if (!res.ok) {
          useToastStore().push({
            tone: "error",
            title: "Download Failed",
            message: "The export artifact could not be downloaded or has expired (24h retention).",
          });
          return;
        }

        const blob = await res.blob();
        const contentDisposition = res.headers.get("Content-Disposition");
        let fileName = `export_${jobId}.zip`;
        if (contentDisposition && contentDisposition.includes("filename=")) {
          const match = contentDisposition.match(/filename="?([^";]+)"?/);
          if (match && match[1]) {
            fileName = match[1];
          }
        }

        const blobUrl = window.URL.createObjectURL(blob);
        const a = document.createElement("a");
        a.href = blobUrl;
        a.download = fileName;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        window.URL.revokeObjectURL(blobUrl);
      } catch (err) {
        useToastStore().apiError("Failed to download export package", err);
      }
    },
  },
});
