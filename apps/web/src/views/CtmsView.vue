<template>
  <div id="section-ctms" class="dashboard-section active">
    <div class="section-header">
      <h2>Clinical Trial Management System (CTMS)</h2>
      <p>
        Monitor operational site status, track site lifecycle milestones,
        recruitment metrics, and coordinate CRA workloads.
      </p>
    </div>

    <div class="grid-2">
      <!-- Site Milestones Card -->
      <div class="card">
        <div class="card-title">Site Operational Milestones</div>
        <div id="ctms-milestones-container" v-html="milestonesHtml" />
        <div style="margin-top: 12px; display: flex; gap: 8px">
          <button
            id="btn-achieve-milestone"
            class="btn btn-primary"
            @click="achieveMilestone"
          >
            Achieve Current Milestone
          </button>
        </div>
      </div>

      <!-- Monitoring Visits Card -->
      <div class="card">
        <div class="card-title">CRA Site Monitoring Visits (MVR)</div>
        <div id="ctms-visits-container" v-html="visitsHtml" />
        <div style="margin-top: 12px; display: flex; gap: 8px">
          <button
            id="btn-schedule-visit"
            class="btn btn-primary"
            @click="scheduleVisit"
          >
            Schedule New Visit
          </button>
          <button id="btn-complete-visit" class="btn" @click="completeVisit">
            Complete Current Visit
          </button>
        </div>
      </div>
    </div>

    <div class="grid-2" style="margin-top: 24px">
      <!-- CRA Allocations & Workload Card -->
      <div class="card">
        <div class="card-title">CRA Allocation & Workload Summaries</div>
        <div id="ctms-workload-container">
          <table class="clinical-visit-matrix">
            <thead>
              <tr>
                <th scope="col">CRA</th>
                <th scope="col">Active Allocations</th>
                <th scope="col">Allocated Sites</th>
                <th scope="col">Allocated Studies</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="a in store.currentCtmsData.allocations" :key="a.cra">
                <td>
                  <strong>{{ a.cra }}</strong>
                </td>
                <td>{{ a.activeAllocations }}</td>
                <td>{{ (a.sites || []).join(", ") }}</td>
                <td>{{ (a.studies || []).join(", ") }}</td>
              </tr>
            </tbody>
          </table>
        </div>
        <div style="margin-top: 12px; display: flex; gap: 8px">
          <button
            id="btn-reallocate-cra"
            class="btn btn-primary"
            @click="reallocateCra"
          >
            Reallocate CRA
          </button>
        </div>
      </div>

      <!-- Site Recruitment metrics Card -->
      <div class="card">
        <div class="card-title">Site Recruitment Metrics</div>
        <div id="ctms-recruitment-container">
          <table class="clinical-visit-matrix">
            <thead>
              <tr>
                <th scope="col">Site ID</th>
                <th scope="col">Screened</th>
                <th scope="col">Enrolled</th>
                <th scope="col">Target</th>
                <th scope="col">Progress</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="r in store.currentCtmsData.recruitment"
                :key="r.siteId"
              >
                <td>
                  <strong>{{ r.siteId }}</strong>
                </td>
                <td>{{ r.screened }}</td>
                <td>{{ r.enrolled }}</td>
                <td>{{ r.target }}</td>
                <td>{{ Math.round((r.enrolled / r.target) * 100) }}%</td>
              </tr>
            </tbody>
          </table>
        </div>
        <div style="margin-top: 12px; display: flex; gap: 8px">
          <button
            id="btn-update-recruitment"
            class="btn btn-primary"
            @click="updateRecruitment"
          >
            Log Recruitment Update
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed } from "vue";
import { useClinicalStore } from "../stores/clinical";
import {
  createCtmsMilestoneTable,
  createCtmsVisitTable,
} from "../lib/legacy_helpers.js";

const store = useClinicalStore();

const milestonesHtml = computed(() => {
  return createCtmsMilestoneTable(store.currentCtmsData.milestones);
});

const visitsHtml = computed(() => {
  return createCtmsVisitTable(store.currentCtmsData.visits);
});

function achieveMilestone() {
  const nextM = store.currentCtmsData.milestones.find(
    (m) => m.status === "PLANNED"
  );
  if (nextM) {
    nextM.status = "ACHIEVED";
    nextM.actualDate = new Date().toISOString().slice(0, 10);
    store.addLedgerBlock(
      "CTMS_MILESTONE_ACHIEVED",
      {
        milestoneType: nextM.type,
        status: nextM.status,
        actualDate: nextM.actualDate,
      },
      `Site operational milestone '${nextM.type}' achieved and verified.`
    );
  } else {
    alert("All milestones have already been achieved!");
  }
}

function scheduleVisit() {
  const newVisit = {
    id: "V" + (store.currentCtmsData.visits.length + 1),
    type: "IMV",
    scheduledDate: new Date(Date.now() + 5 * 24 * 3600 * 1000)
      .toISOString()
      .slice(0, 10),
    actualDate: "",
    status: "SCHEDULED",
    cra: "cra_fderuiter",
  };
  store.currentCtmsData.visits.push(newVisit);
  store.addLedgerBlock(
    "CTMS_VISIT_SCHEDULED",
    {
      visitId: newVisit.id,
      type: newVisit.type,
      scheduledDate: newVisit.scheduledDate,
    },
    `New Monitoring Visit scheduled for ${newVisit.scheduledDate}. Confirmation letter issued.`
  );
}

function completeVisit() {
  const scheduledVisit = store.currentCtmsData.visits.find(
    (v) => v.status === "SCHEDULED"
  );
  if (scheduledVisit) {
    scheduledVisit.status = "SIGNED_OFF";
    scheduledVisit.actualDate = new Date().toISOString().slice(0, 10);
    store.addLedgerBlock(
      "CTMS_VISIT_COMPLETED",
      {
        visitId: scheduledVisit.id,
        type: scheduledVisit.type,
        actualDate: scheduledVisit.actualDate,
      },
      `Monitoring Visit '${scheduledVisit.id}' completed and signed off. Follow-up letter issued.`
    );
  } else {
    alert("No scheduled visits to complete!");
  }
}

function reallocateCra() {
  const craAlice = store.currentCtmsData.allocations.find(
    (a) => a.cra === "cra_alice"
  );
  if (craAlice) {
    if (craAlice.activeAllocations === 1) {
      craAlice.activeAllocations = 2;
      craAlice.sites.push("Site-04");
    } else {
      craAlice.activeAllocations = 1;
      craAlice.sites = ["Site-03"];
    }
    store.addLedgerBlock(
      "CTMS_CRA_REALLOCATION",
      {
        cra: craAlice.cra,
        activeAllocations: craAlice.activeAllocations,
        sites: craAlice.sites,
      },
      `CRA allocations updated to balance workload.`
    );
  }
}

function updateRecruitment() {
  const site1 = store.currentCtmsData.recruitment.find(
    (r) => r.siteId === "Site-01"
  );
  if (site1) {
    site1.screened += 2;
    site1.enrolled += 1;
    store.addLedgerBlock(
      "CTMS_RECRUITMENT_UPDATE",
      {
        siteId: site1.siteId,
        screened: site1.screened,
        enrolled: site1.enrolled,
      },
      `Logged enrollment of new subject at Site-01.`
    );
  }
}
</script>
