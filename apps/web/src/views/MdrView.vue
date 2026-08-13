<template>
  <div id="section-mdr" class="dashboard-section active">
    <div class="section-header">
      <h2>MDR / Protocol Visualizer &amp; Interactive SoA Builder</h2>
      <p>
        Unify upstream clinical study definitions (USDM metadata) directly into
        a visual Schedule of Activities matrix or author elements directly.
      </p>
    </div>

    <!-- Sub-Issue 8 Tab Navigation -->
    <div
      class="tabs-navigation"
      style="
        display: flex;
        gap: 12px;
        margin-bottom: 20px;
        border-bottom: 2px solid var(--border);
        padding-bottom: 10px;
      "
    >
      <button
        class="btn tab-btn-soa"
        :class="activeTab === 'soa' ? 'btn-primary' : 'btn-secondary'"
        @click="activeTab = 'soa'"
      >
        📋 Interactive SoA &amp; USDM
      </button>
      <button
        class="btn tab-btn-mdr"
        :class="activeTab === 'mdr' ? 'btn-primary' : 'btn-secondary'"
        @click="activeTab = 'mdr'"
      >
        🔍 MDR Concept Browse &amp; Edit
      </button>
      <button
        class="btn tab-btn-diff"
        :class="activeTab === 'diff' ? 'btn-primary' : 'btn-secondary'"
        @click="activeTab = 'diff'"
      >
        ⚖️ Alignment &amp; Differences Report
      </button>
    </div>

    <!-- TAB 1: INTERACTIVE SOA WORKSPACE -->
    <div v-if="activeTab === 'soa'">
      <!-- Interactive Builder Controls -->
      <div class="card" style="margin-bottom: 24px; padding: 16px">
        <div
          style="
            display: flex;
            justify-content: space-between;
            align-items: center;
            flex-wrap: wrap;
            gap: 12px;
          "
        >
          <h3 style="font-weight: bold; margin: 0; color: var(--primary)">
            SoA Authoring &amp; Builder Workspace
          </h3>
          <button
            class="btn btn-builder-toggle"
            :class="builderMode ? 'btn-primary' : 'btn-secondary'"
            @click="builderMode = !builderMode"
          >
            {{
              builderMode
                ? "Close Interactive Builder"
                : "🔧 Open Interactive Builder"
            }}
          </button>
        </div>

        <!-- Creator forms -->
        <div
          v-if="builderMode"
          style="
            margin-top: 16px;
            border-top: 1px solid var(--border);
            padding-top: 16px;
          "
        >
          <!-- Error alert banner -->
          <div
            v-if="store.soaError"
            style="
              background-color: #fef2f2;
              border: 1px solid #fca5a5;
              color: #b91c1c;
              padding: 12px;
              border-radius: 6px;
              margin-bottom: 16px;
              font-size: 0.9rem;
            "
          >
            <strong>API Sync Failed (Reverting to Sandbox Local Mode):</strong>
            {{ store.soaError }}
          </div>

          <div
            style="
              display: grid;
              grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
              gap: 16px;
            "
          >
            <!-- Arm Form -->
            <fieldset
              style="
                border: 1px solid var(--border);
                border-radius: 8px;
                padding: 12px;
              "
            >
              <legend style="font-weight: bold; padding: 0 6px">
                Add Study Arm
              </legend>
              <div class="form-group" style="margin-bottom: 8px">
                <label for="new-arm-id">Arm ID</label>
                <input
                  id="new-arm-id"
                  v-model="newArm.id"
                  type="text"
                  placeholder="e.g. ARM-C"
                  style="width: 100%; padding: 6px"
                />
              </div>
              <div class="form-group" style="margin-bottom: 8px">
                <label for="new-arm-name">Arm Name</label>
                <input
                  id="new-arm-name"
                  v-model="newArm.name"
                  type="text"
                  placeholder="e.g. Arm C: High Dose"
                  style="width: 100%; padding: 6px"
                />
              </div>
              <div
                class="form-group"
                style="margin-bottom: 8px; position: relative"
              >
                <label for="new-arm-concept">Arm Type Concept Code</label>
                <input
                  id="new-arm-concept"
                  v-model="newArm.concept_code"
                  type="text"
                  placeholder="Search Arm Type CT..."
                  style="width: 100%; padding: 6px"
                  @input="searchArmTerminology($event.target.value)"
                />
                <!-- Autocomplete Suggestion Dropdown -->
                <div
                  v-if="armSuggestions.length > 0"
                  class="autocomplete-dropdown"
                  style="
                    position: absolute;
                    background: white;
                    border: 1px solid var(--border);
                    border-radius: 4px;
                    width: 100%;
                    z-index: 100;
                    max-height: 150px;
                    overflow-y: auto;
                    box-shadow: 0 4px 6px rgba(0, 0, 0, 0.1);
                  "
                >
                  <div
                    v-for="sug in armSuggestions"
                    :key="sug.concept_code"
                    style="
                      padding: 6px;
                      cursor: pointer;
                      border-bottom: 1px solid rgba(241, 245, 249, 1);
                    "
                    @click="selectArmConcept(sug)"
                  >
                    <strong>{{ sug.concept_code }}</strong> -
                    {{ sug.preferred_name }}
                  </div>
                </div>
              </div>
              <button
                class="btn btn-primary"
                style="width: 100%"
                @click="handleAddArm"
              >
                Add Arm
              </button>
            </fieldset>

            <!-- Epoch Form -->
            <fieldset
              style="
                border: 1px solid var(--border);
                border-radius: 8px;
                padding: 12px;
              "
            >
              <legend style="font-weight: bold; padding: 0 6px">
                Add Epoch
              </legend>
              <div class="form-group" style="margin-bottom: 8px">
                <label for="new-epoch-id">Epoch ID</label>
                <input
                  id="new-epoch-id"
                  v-model="newEpoch.id"
                  type="text"
                  placeholder="e.g. EP-FLW"
                  style="width: 100%; padding: 6px"
                />
              </div>
              <div class="form-group" style="margin-bottom: 8px">
                <label for="new-epoch-name">Epoch Name</label>
                <input
                  id="new-epoch-name"
                  v-model="newEpoch.name"
                  type="text"
                  placeholder="e.g. Follow-up"
                  style="width: 100%; padding: 6px"
                />
              </div>
              <div class="form-group" style="margin-bottom: 8px">
                <label for="new-epoch-seq">Sequence</label>
                <input
                  id="new-epoch-seq"
                  v-model.number="newEpoch.sequence"
                  type="number"
                  style="width: 100%; padding: 6px"
                />
              </div>
              <div class="form-group" style="margin-bottom: 8px">
                <label for="new-epoch-arm">Associated Arm (Optional)</label>
                <select
                  id="new-epoch-arm"
                  v-model="newEpoch.arm_id"
                  style="width: 100%; padding: 6px"
                >
                  <option value="">-- None / Shared --</option>
                  <option
                    v-for="arm in store.currentUsdm.arms"
                    :key="arm.arm_id"
                    :value="arm.arm_id"
                  >
                    {{ arm.arm_name }}
                  </option>
                </select>
              </div>
              <button
                class="btn btn-primary"
                style="width: 100%"
                @click="handleAddEpoch"
              >
                Add Epoch
              </button>
            </fieldset>

            <!-- Visit Form -->
            <fieldset
              style="
                border: 1px solid var(--border);
                border-radius: 8px;
                padding: 12px;
              "
            >
              <legend style="font-weight: bold; padding: 0 6px">
                Add Visit / Encounter
              </legend>
              <div class="form-group" style="margin-bottom: 8px">
                <label for="new-enc-id">Encounter ID</label>
                <input
                  id="new-enc-id"
                  v-model="newEnc.id"
                  type="text"
                  placeholder="e.g. V-WEEK6"
                  style="width: 100%; padding: 6px"
                />
              </div>
              <div class="form-group" style="margin-bottom: 8px">
                <label for="new-enc-name">Encounter Name</label>
                <input
                  id="new-enc-name"
                  v-model="newEnc.name"
                  type="text"
                  placeholder="e.g. Week 6"
                  style="width: 100%; padding: 6px"
                />
              </div>

              <div class="form-group" style="margin-bottom: 8px">
                <label for="new-enc-seq">Sequence</label>
                <input
                  id="new-enc-seq"
                  v-model.number="newEnc.sequence"
                  type="number"
                  style="width: 100%; padding: 6px"
                />
              </div>
              <div
                class="form-group"
                style="margin-bottom: 8px; position: relative"
              >
                <label for="new-enc-concept">Visit Type Concept Code</label>
                <input
                  id="new-enc-concept"
                  v-model="newEnc.concept_code"
                  type="text"
                  placeholder="Search Visit Type CT..."
                  style="width: 100%; padding: 6px"
                  @input="searchEncTerminology($event.target.value)"
                />
                <!-- Autocomplete Suggestion Dropdown -->
                <div
                  v-if="encSuggestions.length > 0"
                  class="autocomplete-dropdown"
                  style="
                    position: absolute;
                    background: white;
                    border: 1px solid var(--border);
                    border-radius: 4px;
                    width: 100%;
                    z-index: 100;
                    max-height: 150px;
                    overflow-y: auto;
                    box-shadow: 0 4px 6px rgba(0, 0, 0, 0.1);
                  "
                >
                  <div
                    v-for="sug in encSuggestions"
                    :key="sug.concept_code"
                    style="
                      padding: 6px;
                      cursor: pointer;
                      border-bottom: 1px solid rgba(241, 245, 249, 1);
                    "
                    @click="selectEncConcept(sug)"
                  >
                    <strong>{{ sug.concept_code }}</strong> -
                    {{ sug.preferred_name }}
                  </div>
                </div>
              </div>
              <div class="form-group" style="margin-bottom: 8px">
                <label for="new-enc-epoch">Associated Epoch</label>
                <select
                  id="new-enc-epoch"
                  v-model="newEnc.epoch_id"
                  style="width: 100%; padding: 6px"
                >
                  <option value="">-- Select Epoch --</option>
                  <option
                    v-for="ep in store.currentUsdm.epochs"
                    :key="ep.epoch_id"
                    :value="ep.epoch_id"
                  >
                    {{ ep.epoch_name }}
                  </option>
                </select>
              </div>
              <button
                class="btn btn-primary"
                style="width: 100%"
                @click="handleAddEncounter"
              >
                Add Visit
              </button>
            </fieldset>

            <!-- Procedure Form -->
            <fieldset
              style="
                border: 1px solid var(--border);
                border-radius: 8px;
                padding: 12px;
              "
            >
              <legend style="font-weight: bold; padding: 0 6px">
                Add Activity / Procedure
              </legend>
              <div class="form-group" style="margin-bottom: 8px">
                <label for="new-proc-id">Activity ID</label>
                <input
                  id="new-proc-id"
                  v-model="newProc.id"
                  type="text"
                  placeholder="e.g. ACT-LAB"
                  style="width: 100%; padding: 6px"
                />
              </div>
              <div class="form-group" style="margin-bottom: 8px">
                <label for="new-proc-name">Activity Name</label>
                <input
                  id="new-proc-name"
                  v-model="newProc.name"
                  type="text"
                  placeholder="e.g. Laboratory Blood Draw"
                  style="width: 100%; padding: 6px"
                />
              </div>
              <button
                class="btn btn-primary"
                style="width: 100%"
                @click="handleAddProcedure"
              >
                Add Procedure
              </button>
            </fieldset>
          </div>

          <!-- Custom Interactive Link & Triggering Conditions -->
          <fieldset
            style="
              border: 1px solid var(--border);
              border-radius: 8px;
              padding: 12px;
              margin-top: 16px;
            "
          >
            <legend style="font-weight: bold; padding: 0 6px">
              Applicability, Custom Timing, and Arm Filtering
            </legend>
            <div class="grid-2-responsive">
              <div class="form-group">
                <label for="link-procedure">Select Procedure</label>
                <select
                  id="link-procedure"
                  v-model="linkPayload.procedure_id"
                  style="width: 100%; padding: 6px"
                >
                  <option value="">-- Select Procedure --</option>
                  <option
                    v-for="row in store.currentUsdm.rows"
                    :key="row.activity_id"
                    :value="row.activity_id"
                  >
                    {{ row.activity_name }}
                  </option>
                </select>
              </div>
              <div class="form-group">
                <label for="link-visit">Select Visit</label>
                <select
                  id="link-visit"
                  v-model="linkPayload.visit_id"
                  style="width: 100%; padding: 6px"
                >
                  <option value="">-- Select Visit --</option>
                  <option
                    v-for="enc in store.currentUsdm.encounters"
                    :key="enc.encounter_id"
                    :value="enc.encounter_id"
                  >
                    {{ enc.encounter_name }}
                  </option>
                </select>
              </div>
            </div>
            <div class="form-group" style="margin-top: 8px">
              <label for="link-timing"
                >Custom Timing Window / Details (e.g. "Within 10 mins", "Day
                1")</label
              >
              <input
                id="link-timing"
                v-model="linkPayload.timing"
                type="text"
                placeholder="Leave empty for default applicability"
                style="width: 100%; padding: 6px"
              />
            </div>
            <div
              style="
                margin-top: 12px;
                display: flex;
                gap: 8px;
                justify-content: flex-end;
              "
            >
              <button
                class="btn"
                style="background-color: var(--error); color: white"
                @click="handleToggleApplicability(false)"
              >
                Remove Applicability
              </button>
              <button
                class="btn btn-primary"
                @click="handleToggleApplicability(true)"
              >
                Apply Applicability &amp; Timing
              </button>
            </div>
          </fieldset>
        </div>
      </div>

      <!-- Visual protocol editor layout -->
      <div class="grid-2-responsive">
        <!-- USDM study JSON -->
        <div class="card json-editor-container">
          <div class="card-title">
            <span>CDISC USDM Study Protocol JSON</span>
            <button
              id="btn-reset-usdm"
              class="badge"
              style="
                cursor: pointer;
                background-color: var(--accent);
                color: white;
                border: none;
              "
              @click="resetUsdm"
            >
              Reset Mock JSON
            </button>
          </div>
          <textarea
            id="usdm-json"
            v-model="usdmText"
            class="json-editor"
            spellcheck="false"
            aria-label="CDISC USDM Study Protocol JSON"
          />
          <p style="font-size: 0.8rem; color: #64748b; margin-top: 8px">
            Edit this mock USDM JSON definition and click "Update Visualizer"
            below to dynamically re-render the Schedule of Activities Matrix.
          </p>
          <div
            style="margin-top: 12px; display: flex; justify-content: flex-end"
          >
            <button
              id="btn-update-soa"
              class="btn btn-primary"
              @click="updateSoa"
            >
              Update Visualizer
            </button>
          </div>
        </div>

        <!-- Schedule of Activities / eCRF Canvas -->
        <div class="card">
          <div
            class="card-header"
            style="
              display: flex;
              justify-content: space-between;
              align-items: center;
              border-bottom: 1px solid var(--border);
              padding-bottom: 12px;
              margin-bottom: 16px;
            "
          >
            <div style="display: flex; gap: 8px; align-items: center">
              <span class="card-title" style="margin-right: 12px"
                >Schedule of Activities (SoA) Matrix</span
              >
              <button
                id="btn-tab-soa"
                class="btn"
                :class="!showCanvas ? 'btn-primary' : 'btn-secondary'"
                style="font-size: 0.85rem; padding: 6px 12px"
                @click="showCanvas = false"
              >
                SoA Matrix
              </button>
              <button
                id="btn-tab-canvas"
                class="btn"
                :class="showCanvas ? 'btn-primary' : 'btn-secondary'"
                style="font-size: 0.85rem; padding: 6px 12px"
                @click="showCanvas = true"
              >
                eCRF Canvas
              </button>
            </div>
            <span
              v-if="store.soaLoading"
              style="font-size: 0.8rem; font-weight: normal; color: #64748b"
              >(Syncing...)</span
            >
          </div>

          <div v-if="!showCanvas" id="soa-matrix-container">
            <ClinicalSoAMatrix :soa-data="soaData" />
            <ClinicalGanttVisualizer />
          </div>

          <div v-else id="crf-canvas-container">
            <CrfAuthoringCanvas
              :form-schema="formSchema"
              :selected-field-id="selectedFieldId"
              @select-field="onSelectField"
              @update-schema="onUpdateSchema"
            />
          </div>
        </div>
      </div>
    </div>

    <!-- TAB 2: MDR CONCEPT BROWSE & EDIT (Sub-Issue 8) -->
    <div v-else-if="activeTab === 'mdr'" class="card">
      <div class="card-title">
        NCI Thesaurus Concept Registry &amp; Terminology Browser
      </div>
      <p style="font-size: 0.85rem; color: #475569; margin-bottom: 16px">
        Search the NCI Thesaurus (EVS) for standard controlled terminology
        concepts, or author new study concepts directly.
      </p>

      <div class="grid-asymmetric-responsive">
        <!-- Concept Search / Browse -->
        <div style="border-right: 1px solid var(--border); padding-right: 24px">
          <h3
            style="
              font-weight: bold;
              font-size: 1rem;
              margin-bottom: 12px;
              color: var(--primary);
            "
          >
            Search Concept Codes
          </h3>
          <div class="form-group" style="margin-bottom: 16px">
            <label for="concept-search-input"
              >Search Term (e.g. "Arm", "Encounter")</label
            >
            <div style="display: flex; gap: 8px">
              <input
                id="concept-search-input"
                v-model="conceptSearchQuery"
                type="text"
                placeholder="Type search term..."
                style="
                  flex: 1;
                  padding: 8px;
                  border: 1px solid var(--border);
                  border-radius: 4px;
                "
                @keyup.enter="searchConcepts"
              />
              <button class="btn btn-primary" @click="searchConcepts">
                Search
              </button>
            </div>
          </div>

          <div
            v-if="searchingConcepts"
            style="font-size: 0.85rem; color: #64748b"
          >
            Searching terminology EVS client...
          </div>
          <div
            v-else-if="conceptResults.length === 0"
            style="font-size: 0.85rem; color: #64748b; font-style: italic"
          >
            No results found.
          </div>
          <div v-else style="max-height: 400px; overflow-y: auto">
            <ul
              style="
                list-style: none;
                padding: 0;
                display: flex;
                flex-direction: column;
                gap: 8px;
              "
            >
              <li
                v-for="c in conceptResults"
                :key="c.concept_code"
                style="
                  padding: 10px;
                  border: 1px solid var(--border);
                  border-radius: 6px;
                  cursor: pointer;
                  background-color: #f8fafc;
                "
                @click="selectConceptForEdit(c)"
              >
                <div
                  style="
                    display: flex;
                    justify-content: space-between;
                    font-weight: bold;
                    font-size: 0.85rem;
                    margin-bottom: 4px;
                  "
                >
                  <span style="color: var(--accent)">{{ c.concept_code }}</span>
                  <span>{{ c.preferred_name }}</span>
                </div>
                <div style="font-size: 0.75rem; color: #475569">
                  {{ c.definition || "No definition available." }}
                </div>
              </li>
            </ul>
          </div>
        </div>

        <!-- Create or Edit Concept Form -->
        <div>
          <h3
            style="
              font-weight: bold;
              font-size: 1rem;
              margin-bottom: 12px;
              color: var(--primary);
            "
          >
            {{
              editingConceptCode
                ? "Edit Concept Definition"
                : "Author New Concept"
            }}
          </h3>
          <div style="display: flex; flex-direction: column; gap: 12px">
            <div class="form-group">
              <label for="concept-code-input">Concept Code</label>
              <input
                id="concept-code-input"
                v-model="conceptForm.concept_code"
                type="text"
                placeholder="e.g. C12345"
                :disabled="!!editingConceptCode"
                style="
                  width: 100%;
                  padding: 8px;
                  border: 1px solid var(--border);
                  border-radius: 4px;
                "
              />
            </div>
            <div class="form-group">
              <label for="concept-name-input">Preferred Name</label>
              <input
                id="concept-name-input"
                v-model="conceptForm.preferred_name"
                type="text"
                placeholder="e.g. Active Arm C"
                style="
                  width: 100%;
                  padding: 8px;
                  border: 1px solid var(--border);
                  border-radius: 4px;
                "
              />
            </div>
            <div class="form-group">
              <label for="concept-def-input">Definition</label>
              <textarea
                id="concept-def-input"
                v-model="conceptForm.definition"
                placeholder="Detailed clinical definition..."
                rows="3"
                style="
                  width: 100%;
                  padding: 8px;
                  border: 1px solid var(--border);
                  border-radius: 4px;
                "
              />
            </div>
            <div
              style="
                display: flex;
                justify-content: flex-end;
                gap: 8px;
                margin-top: 8px;
              "
            >
              <button
                v-if="editingConceptCode"
                class="btn btn-secondary"
                @click="resetConceptForm"
              >
                Cancel Edit
              </button>
              <button class="btn btn-primary" @click="handleSaveConcept">
                {{
                  editingConceptCode
                    ? "Save Signed Changes"
                    : "Create &amp; Register Concept"
                }}
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- TAB 3: ALIGNMENT & DIFFERENCES REPORT (Sub-Issue 8) -->
    <div v-else-if="activeTab === 'diff'" class="card">
      <div class="card-title">
        Study Protocol Alignment &amp; Differences Report
      </div>
      <p style="font-size: 0.85rem; color: #475569; margin-bottom: 16px">
        Compare the current active study schema (USDM version) against the last
        approved study baseline to audit and verify differences.
      </p>

      <div
        style="
          background-color: #f8fafc;
          border: 1px solid var(--border);
          border-radius: 8px;
          padding: 16px;
          margin-bottom: 24px;
        "
      >
        <div
          style="
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin-bottom: 12px;
          "
        >
          <span
            style="font-weight: bold; font-size: 0.95rem; color: var(--primary)"
            >Comparison Baseline: Last Approved Study Version (v1.0)</span
          >
          <span class="badge lookup-valid" style="font-size: 0.75rem"
            >Aligned and Approved</span
          >
        </div>
        <div
          style="
            display: grid;
            grid-template-columns: repeat(4, 1fr);
            gap: 16px;
            text-align: center;
          "
        >
          <div style="border-right: 1px solid var(--border)">
            <div style="font-size: 0.8rem; color: #64748b">
              Baseline Study Arms
            </div>
            <div
              style="
                font-size: 1.5rem;
                font-weight: bold;
                color: var(--primary);
              "
            >
              2
            </div>
          </div>
          <div style="border-right: 1px solid var(--border)">
            <div style="font-size: 0.8rem; color: #64748b">Baseline Epochs</div>
            <div
              style="
                font-size: 1.5rem;
                font-weight: bold;
                color: var(--primary);
              "
            >
              3
            </div>
          </div>
          <div style="border-right: 1px solid var(--border)">
            <div style="font-size: 0.8rem; color: #64748b font-weight: normal;">
              Baseline Visits
            </div>
            <div
              style="
                font-size: 1.5rem;
                font-weight: bold;
                color: var(--primary);
              "
            >
              4
            </div>
          </div>
          <div>
            <div style="font-size: 0.8rem; color: #64748b">
              Baseline Procedures
            </div>
            <div
              style="
                font-size: 1.5rem;
                font-weight: bold;
                color: var(--primary);
              "
            >
              4
            </div>
          </div>
        </div>
      </div>

      <h3
        style="
          font-weight: bold;
          font-size: 1.1rem;
          margin-bottom: 12px;
          color: var(--primary);
        "
      >
        Detected Structural Differences / Amendments
      </h3>
      <div
        v-if="differences.length === 0"
        style="
          padding: 24px;
          border: 1px dashed var(--border);
          text-align: center;
          color: #64748b;
          font-style: italic;
        "
      >
        No amendments or changes detected. Protocol schema is fully aligned with
        approved baseline.
      </div>
      <div v-else style="display: flex; flex-direction: column; gap: 10px">
        <div
          v-for="(diff, index) in differences"
          :key="index"
          style="
            display: flex;
            justify-content: space-between;
            align-items: center;
            padding: 12px;
            border: 1px solid var(--border);
            border-radius: 8px;
            background-color: white;
          "
        >
          <div>
            <span
              class="badge"
              :class="diff.type === 'added' ? 'lookup-valid' : 'lookup-invalid'"
              style="margin-right: 12px; font-size: 0.75rem"
            >
              {{ diff.type.toUpperCase() }}
            </span>
            <strong style="font-size: 0.9rem">{{ diff.element }}</strong>
            <span style="font-size: 0.85rem; color: #475569; margin-left: 8px"
              >({{ diff.description }})</span
            >
          </div>
          <span
            style="font-size: 0.8rem; font-family: monospace; color: #64748b"
            >Path: /usdm/{{ diff.category }}/{{ diff.id }}</span
          >
        </div>
      </div>
    </div>

    <!-- Part 11 Change Reason Modal -->
    <ReasonModal
      :show="showReasonModal"
      title="Reason for Change Required"
      description="To comply with 21 CFR Part 11 / EU Annex 11, you must document a reason for changing this clinical study design."
      :options="mdrReasonOptions"
      default-option="Initial Entry"
      @confirm="confirmMutation"
      @cancel="cancelMutation"
    />

    <!-- Guided Onboarding Tour -->
    <OnboardingTour v-model:activeTab="activeTab" />
  </div>
</template>

<script setup>
import { ref, computed, watch, reactive } from "vue";
import { useClinicalStore } from "../stores/clinical";
import ClinicalSoAMatrix from "../components/clinical/ClinicalSoAMatrix.vue";
import ClinicalGanttVisualizer from "../components/clinical/ClinicalGanttVisualizer.vue";
import { terminologyClient } from "../api/terminologyClient.js";
import { debounce } from "ui";
import ReasonModal from "../components/ReasonModal.vue";
import CrfAuthoringCanvas from "../components/crf/CrfAuthoringCanvas.vue";
import { useDesignerStore } from "../stores/designer.js";
import OnboardingTour from "../components/OnboardingTour.vue";

const mdrReasonOptions = [
  { value: "Initial Entry", text: "Initial Study Configuration" },
  { value: "Protocol Amendment", text: "Protocol Amendment / Fork" },
  { value: "Correction of Error", text: "Correction of study layout error" },
  { value: "Other", text: "Other (specify below)" },
];

const store = useClinicalStore();
const designerStore = useDesignerStore();

const selectedFieldId = computed(() => designerStore.selectedFieldId);
const formSchema = computed({
  get: () => designerStore.activeForm,
  set: (val) => {
    designerStore.activeForm = val;
  },
});
const showCanvas = ref(false);

function onSelectField(fieldId) {
  designerStore.setSelectedFieldId(fieldId);
}

function onUpdateSchema(newSchema) {
  designerStore.activeForm = newSchema;
}

// Sub-Issue 8 States
const activeTab = ref("soa"); // 'soa', 'mdr', 'diff'
const conceptSearchQuery = ref("");
const searchingConcepts = ref(false);
const conceptResults = ref([]);
const editingConceptCode = ref(null);
const conceptForm = reactive({
  concept_code: "",
  preferred_name: "",
  definition: "",
});

const armSuggestions = ref([]);
const encSuggestions = ref([]);

// Difference calculation against baseline
const differences = computed(() => {
  const diffs = [];
  const baseArms = ["ARM-A", "ARM-B"];
  const baseEpochs = ["EP-SCR", "EP-TRT-A", "EP-TRT-B"];
  const baseVisits = ["V-SCR", "V-TRT-A1", "V-TRT-A2", "V-TRT-B1"];
  const baseProcedures = ["ACT-DEM", "ACT-VS", "ACT-AE", "ACT-MED"];

  const currentArms = (store.currentUsdm.arms || []).map((a) => a.arm_id);
  const currentEpochs = (store.currentUsdm.epochs || []).map((e) => e.epoch_id);
  const currentVisits = (store.currentUsdm.encounters || []).map(
    (v) => v.encounter_id
  );
  const currentProcedures = (store.currentUsdm.rows || []).map(
    (r) => r.activity_id
  );

  // Added Arms
  currentArms.forEach((id) => {
    if (!baseArms.includes(id)) {
      const aObj = store.currentUsdm.arms.find((a) => a.arm_id === id);
      diffs.push({
        type: "added",
        category: "arms",
        id,
        element: `Study Arm: ${aObj ? aObj.arm_name : id}`,
        description: "New study arm introduced in protocol amendment.",
      });
    }
  });

  // Added Epochs
  currentEpochs.forEach((id) => {
    if (!baseEpochs.includes(id)) {
      const eObj = store.currentUsdm.epochs.find((e) => e.epoch_id === id);
      diffs.push({
        type: "added",
        category: "epochs",
        id,
        element: `Study Epoch: ${eObj ? eObj.epoch_name : id}`,
        description: "New epoch segment added.",
      });
    }
  });

  // Added Visits
  currentVisits.forEach((id) => {
    if (!baseVisits.includes(id)) {
      const vObj = store.currentUsdm.encounters.find(
        (v) => v.encounter_id === id
      );
      diffs.push({
        type: "added",
        category: "encounters",
        id,
        element: `Visit/Encounter: ${vObj ? vObj.encounter_name : id}`,
        description: "New clinical site visit defined.",
      });
    }
  });

  // Added Procedures
  currentProcedures.forEach((id) => {
    if (!baseProcedures.includes(id)) {
      const pObj = store.currentUsdm.rows.find((p) => p.activity_id === id);
      diffs.push({
        type: "added",
        category: "rows",
        id,
        element: `Procedure/Activity: ${pObj ? pObj.activity_name : id}`,
        description: "New CDASH electronic case report form procedure mapped.",
      });
    }
  });

  return diffs;
});

// Search terminology lookups
async function searchConcepts() {
  if (!conceptSearchQuery.value || !conceptSearchQuery.value.trim()) {
    conceptResults.value = [];
    return;
  }
  searchingConcepts.value = true;
  try {
    const res = await terminologyClient.searchTerminology(
      conceptSearchQuery.value.trim(),
      { changeReason: "Browse concepts" }
    );
    conceptResults.value = res.results || [];
  } catch (err) {
    console.warn("Terminology search failed:", err);
    conceptResults.value = [
      {
        concept_code: "C25406",
        preferred_name: "Arm segment",
        definition: "A segment of a study arm description.",
      },
      {
        concept_code: "C49645",
        preferred_name: "Encounter visit",
        definition: "A scheduled visit or clinical encounter.",
      },
    ];
  } finally {
    searchingConcepts.value = false;
  }
}

function selectConceptForEdit(c) {
  editingConceptCode.value = c.concept_code;
  conceptForm.concept_code = c.concept_code;
  conceptForm.preferred_name = c.preferred_name;
  conceptForm.definition = c.definition || "";
}

function resetConceptForm() {
  editingConceptCode.value = null;
  conceptForm.concept_code = "";
  conceptForm.preferred_name = "";
  conceptForm.definition = "";
}

// Saving concepts triggers ReasonModal
function handleSaveConcept() {
  if (!conceptForm.concept_code || !conceptForm.preferred_name) {
    alert("Please populate Concept Code and Preferred Name");
    return;
  }
  queueMutation({
    type: "concept",
    id: conceptForm.concept_code.trim(),
    properties: {
      preferred_name: conceptForm.preferred_name.trim(),
      definition: conceptForm.definition.trim(),
    },
  });
}

const debouncedSearchArm = debounce(async (term) => {
  if (!term || !term.trim()) {
    armSuggestions.value = [];
    return;
  }
  try {
    const res = await terminologyClient.searchTerminology(term, {
      changeReason: "Arm concept search",
    });
    armSuggestions.value = res.results || [];
  } catch (err) {
    console.warn("Failed to search arm terminology:", err);
  }
}, 300);

const debouncedSearchEnc = debounce(async (term) => {
  if (!term || !term.trim()) {
    encSuggestions.value = [];
    return;
  }
  try {
    const res = await terminologyClient.searchTerminology(term, {
      changeReason: "Encounter concept search",
    });
    encSuggestions.value = res.results || [];
  } catch (err) {
    console.warn("Failed to search encounter/visit terminology:", err);
  }
}, 300);

function searchArmTerminology(term) {
  debouncedSearchArm(term);
}

function searchEncTerminology(term) {
  debouncedSearchEnc(term);
}

function selectArmConcept(sug) {
  newArm.concept_code = sug.concept_code;
  armSuggestions.value = [];
}

function selectEncConcept(sug) {
  newEnc.concept_code = sug.concept_code;
  encSuggestions.value = [];
}

const builderMode = ref(false);
const usdmText = ref(JSON.stringify(store.currentUsdm, null, 2));

// Creation Forms States
const newArm = reactive({ id: "", name: "", concept_code: "" });
const newEpoch = reactive({ id: "", name: "", sequence: 1, arm_id: "" });
const newEnc = reactive({
  id: "",
  name: "",
  sequence: 1,
  epoch_id: "",
  concept_code: "",
});
const newProc = reactive({ id: "", name: "" });

// Link Applicability States
const linkPayload = reactive({ procedure_id: "", visit_id: "", timing: "" });

// Part 11 Reason Modal States
const showReasonModal = ref(false);
const pendingMutation = ref(null);

watch(
  () => store.currentUsdm,
  (newVal) => {
    usdmText.value = JSON.stringify(newVal, null, 2);
  },
  { deep: true }
);

const soaData = computed(() => {
  try {
    const parsed = JSON.parse(usdmText.value);
    if (!parsed) return null;
    if (parsed.rows || parsed.encounters || parsed.epochs) {
      return parsed;
    }
    // Adapt flat/old shape (visits, forms) to the rich shape
    const visits = parsed.visits || [];
    const forms = parsed.forms || [];
    const encounters = visits.map((v, idx) => ({
      encounter_id: v,
      encounter_name: v,
      epoch_id: "EP-DEFAULT",
      sequence: idx + 1,
    }));
    const epochs = [
      { epoch_id: "EP-DEFAULT", epoch_name: "Default Epoch", sequence: 1 },
    ];
    const rows = forms.map((form) => ({
      activity_id: form.name,
      activity_name: form.name,
      cells: form.statuses.map((status, idx) => ({
        encounter_id: visits[idx],
        is_applicable: status === "Complete" || status === "Pending",
        details: status,
      })),
    }));
    return {
      epochs,
      encounters,
      rows,
      arms: [],
    };
  } catch {
    return null;
  }
});

function resetUsdm() {
  const defaultUsdm = {
    studyId: "STUDY-USDM-001",
    studyTitle: "Phase II Trial of Cadence-001 in Essential Hypertension",
    objectives: [
      {
        id: "OBJ-001",
        type: "Primary",
        description:
          "Evaluate the reduction of mean sitting Systolic Blood Pressure (SBP) from baseline.",
      },
      {
        id: "OBJ-002",
        type: "Secondary",
        description:
          "Evaluate safety and tolerability of daily oral administration of Cadence-001.",
      },
    ],
    arms: [
      { arm_id: "ARM-A", arm_name: "Arm A: Active 10mg daily" },
      { arm_id: "ARM-B", arm_name: "Arm B: Placebo Control" },
    ],
    epochs: [
      { epoch_id: "EP-SCR", epoch_name: "Screening", sequence: 1 },
      {
        epoch_id: "EP-TRT-A",
        epoch_name: "Treatment Phase",
        sequence: 2,
        arm_id: "ARM-A",
      },
      {
        epoch_id: "EP-TRT-B",
        epoch_name: "Treatment Phase",
        sequence: 2,
        arm_id: "ARM-B",
      },
    ],
    encounters: [
      {
        encounter_id: "V-SCR",
        encounter_name: "Day -7 to -1",
        epoch_id: "EP-SCR",
        sequence: 1,
      },
      {
        encounter_id: "V-TRT-A1",
        encounter_name: "Week 2",
        epoch_id: "EP-TRT-A",
        sequence: 2,
      },
      {
        encounter_id: "V-TRT-A2",
        encounter_name: "Week 4",
        epoch_id: "EP-TRT-A",
        sequence: 3,
      },
      {
        encounter_id: "V-TRT-B1",
        encounter_name: "Week 2",
        epoch_id: "EP-TRT-B",
        sequence: 4,
      },
    ],
    rows: [
      {
        activity_id: "ACT-DEM",
        activity_name: "Informed Consent & Demographics",
        cells: [
          { encounter_id: "V-SCR", is_applicable: true, details: "Mandatory" },
          { encounter_id: "V-TRT-A1", is_applicable: false },
          { encounter_id: "V-TRT-A2", is_applicable: false },
          { encounter_id: "V-TRT-B1", is_applicable: false },
        ],
      },
      {
        activity_id: "ACT-VS",
        activity_name: "Vital Signs (BP & Pulse)",
        cells: [
          { encounter_id: "V-SCR", is_applicable: true, details: "Day -7" },
          {
            encounter_id: "V-TRT-A1",
            is_applicable: true,
            details: "Within 10 mins",
          },
          {
            encounter_id: "V-TRT-A2",
            is_applicable: true,
            details: "Conditional",
          },
          {
            encounter_id: "V-TRT-B1",
            is_applicable: true,
            details: "Within 10 mins",
          },
        ],
      },
      {
        activity_id: "ACT-AE",
        activity_name: "Adverse Events Check",
        cells: [
          { encounter_id: "V-SCR", is_applicable: false },
          {
            encounter_id: "V-TRT-A1",
            is_applicable: true,
            details: "Continuous",
          },
          {
            encounter_id: "V-TRT-A2",
            is_applicable: true,
            details: "Continuous",
          },
          {
            encounter_id: "V-TRT-B1",
            is_applicable: true,
            details: "Optional",
          },
        ],
      },
      {
        activity_id: "ACT-MED",
        activity_name: "Study Medication Log",
        cells: [
          { encounter_id: "V-SCR", is_applicable: false },
          {
            encounter_id: "V-TRT-A1",
            is_applicable: true,
            details: "Daily entry",
          },
          {
            encounter_id: "V-TRT-A2",
            is_applicable: true,
            details: "Daily entry",
          },
          {
            encounter_id: "V-TRT-B1",
            is_applicable: true,
            details: "Daily entry",
          },
        ],
      },
    ],
  };
  store.currentUsdm = JSON.parse(JSON.stringify(defaultUsdm));
  store.addLedgerBlock(
    "USDM_RESET",
    { studyId: store.currentUsdm.studyId },
    "User reset study protocol schema back to default USDM v3.0 specs."
  );
}

function updateSoa() {
  try {
    const parsed = JSON.parse(usdmText.value);
    if (!parsed.visits && !parsed.encounters) {
      alert(
        "Invalid USDM Structure! Must contain either 'visits' or 'encounters'."
      );
      return;
    }
    store.currentUsdm = parsed;
    store.addLedgerBlock(
      "USDM_UPDATE",
      { studyId: store.currentUsdm.studyId },
      "User modified and compiled a custom USDM study protocol."
    );
  } catch (err) {
    alert("Parsing Error: " + err.message);
  }
}

// Interactive Creator Handlers
function queueMutation(mutation) {
  // Validate instantly on the spot before opening the justification modal!
  let payloadToValidate = null;
  let validationType = mutation.type;

  if (mutation.type === "arms") {
    payloadToValidate = { id: mutation.id, ...mutation.properties };
  } else if (mutation.type === "epochs") {
    payloadToValidate = { id: mutation.id, ...mutation.properties };
  } else if (mutation.type === "visits") {
    payloadToValidate = { id: mutation.id, ...mutation.properties };
  } else if (mutation.type === "procedures") {
    payloadToValidate = { id: mutation.id, ...mutation.properties };
  } else if (mutation.type === "concept") {
    payloadToValidate = {
      code: mutation.id,
      codeSystem: mutation.properties.codeSystem || "NCI_Thesaurus",
      codeSystemVersion: mutation.properties.codeSystemVersion || "24.03d",
      decode:
        mutation.properties.decode || mutation.properties.preferred_name || "",
    };
  }

  if (payloadToValidate) {
    const validation = store.validateModel(validationType, payloadToValidate);
    if (!validation.success) {
      const errorMsg = `Instant Schema Validation Error: ${validation.error.errors
        .map((e) => `${e.path.join(".") || "field"}: ${e.message}`)
        .join(", ")}`;
      alert(errorMsg);
      console.error(errorMsg);
      return; // Do not proceed to showReasonModal or save
    }
  }

  pendingMutation.value = mutation;
  showReasonModal.value = true;
}

function cancelMutation() {
  showReasonModal.value = false;
  pendingMutation.value = null;
}

async function confirmMutation(finalReason) {
  if (!pendingMutation.value) return;

  showReasonModal.value = false;
  const mutation = pendingMutation.value;
  pendingMutation.value = null;

  try {
    if (mutation.type === "link") {
      await store.pushSoALink(mutation.linkType, mutation.payload, finalReason);
    } else if (mutation.type === "concept") {
      // Create/Edit Concept Terminology Action
      await store.addLedgerBlock(
        "CONCEPT_AUTHOR",
        { concept_code: mutation.id, properties: mutation.properties },
        finalReason
      );
      // Simulate registering locally
      if (!editingConceptCode.value) {
        conceptResults.value.push({
          concept_code: mutation.id,
          preferred_name: mutation.properties.preferred_name,
          definition: mutation.properties.definition,
        });
      } else {
        const found = conceptResults.value.find(
          (cr) => cr.concept_code === mutation.id
        );
        if (found) {
          found.preferred_name = mutation.properties.preferred_name;
          found.definition = mutation.properties.definition;
        }
      }
      resetConceptForm();
      alert("Concept terminology registration successful!");
    } else {
      await store.pushSoAMutation(
        mutation.type,
        mutation.id,
        mutation.properties,
        finalReason
      );
    }
    // Local fallback replication in sandbox mode
    applyMutationLocally(mutation);
  } catch (err) {
    console.warn(
      "API save failed, mutation preserved locally inside browser:",
      err
    );
    applyMutationLocally(mutation);
  }
}

function applyMutationLocally(mutation) {
  if (mutation.type === "arms") {
    if (!store.currentUsdm.arms) store.currentUsdm.arms = [];
    store.currentUsdm.arms.push({
      arm_id: mutation.id,
      arm_name: mutation.properties.name,
    });
  } else if (mutation.type === "epochs") {
    if (!store.currentUsdm.epochs) store.currentUsdm.epochs = [];
    store.currentUsdm.epochs.push({
      epoch_id: mutation.id,
      epoch_name: mutation.properties.name,
      sequence: mutation.properties.sequence,
      arm_id: mutation.properties.arm_id || undefined,
    });
  } else if (mutation.type === "visits") {
    if (!store.currentUsdm.encounters) store.currentUsdm.encounters = [];
    store.currentUsdm.encounters.push({
      encounter_id: mutation.id,
      encounter_name: mutation.properties.name,
      epoch_id: mutation.properties.epoch_id,
      sequence: mutation.properties.sequence,
    });
  } else if (mutation.type === "procedures") {
    if (!store.currentUsdm.rows) store.currentUsdm.rows = [];
    store.currentUsdm.rows.push({
      activity_id: mutation.id,
      activity_name: mutation.properties.name,
      cells: store.currentUsdm.encounters.map((e) => ({
        encounter_id: e.encounter_id,
        is_applicable: false,
      })),
    });
  } else if (mutation.type === "link") {
    const { procedure_id, visit_id, is_applicable, timing } = mutation.payload;
    const row = store.currentUsdm.rows.find(
      (r) => r.activity_id === procedure_id
    );
    if (row) {
      let cell = row.cells.find((c) => c.encounter_id === visit_id);
      if (!cell) {
        cell = { encounter_id: visit_id, is_applicable: false };
        row.cells.push(cell);
      }
      cell.is_applicable = is_applicable;
      cell.details = is_applicable ? timing || undefined : undefined;
    }
  }
  // Force reactivity update
  store.currentUsdm = { ...store.currentUsdm };
}

function handleAddArm() {
  if (!newArm.id || !newArm.name) {
    alert("Please enter Arm ID and Arm Name");
    return;
  }
  queueMutation({
    type: "arms",
    id: newArm.id.trim(),
    properties: { name: newArm.name.trim() },
  });
  newArm.id = "";
  newArm.name = "";
  newArm.concept_code = "";
}

function handleAddEpoch() {
  if (!newEpoch.id || !newEpoch.name) {
    alert("Please enter Epoch ID and Epoch Name");
    return;
  }
  queueMutation({
    type: "epochs",
    id: newEpoch.id.trim(),
    properties: {
      name: newEpoch.name.trim(),
      sequence: newEpoch.sequence,
      arm_id: newEpoch.arm_id || null,
    },
  });
  newEpoch.id = "";
  newEpoch.name = "";
  newEpoch.sequence = store.currentUsdm.epochs
    ? store.currentUsdm.epochs.length + 1
    : 1;
}

function handleAddEncounter() {
  if (!newEnc.id || !newEnc.name || !newEnc.epoch_id) {
    alert("Please populate all fields");
    return;
  }
  queueMutation({
    type: "visits",
    id: newEnc.id.trim(),
    properties: {
      name: newEnc.name.trim(),
      sequence: newEnc.sequence,
      epoch_id: newEnc.epoch_id,
    },
  });
  newEnc.id = "";
  newEnc.name = "";
  newEnc.concept_code = "";
  newEnc.sequence = store.currentUsdm.encounters
    ? store.currentUsdm.encounters.length + 1
    : 1;
  newEnc.concept_code = "";
}

function handleAddProcedure() {
  if (!newProc.id || !newProc.name) {
    alert("Please enter Activity ID and Name");
    return;
  }
  queueMutation({
    type: "procedures",
    id: newProc.id.trim(),
    properties: { name: newProc.name.trim() },
  });
  newProc.id = "";
  newProc.name = "";
}

function handleToggleApplicability(isApplicable) {
  if (!linkPayload.procedure_id || !linkPayload.visit_id) {
    alert("Please select both a Procedure and a Visit first");
    return;
  }
  queueMutation({
    type: "link",
    linkType: isApplicable ? "visit-procedure" : "epoch-visit", // Toggle link family types
    payload: {
      procedure_id: linkPayload.procedure_id,
      visit_id: linkPayload.visit_id,
      is_applicable: isApplicable,
      timing: linkPayload.timing.trim() || undefined,
    },
  });
}
</script>
