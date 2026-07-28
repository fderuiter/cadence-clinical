<template>
  <div
    id="section-rules"
    class="dashboard-section active"
  >
    <div class="section-header">
      <h2>Interactive Rules Designer</h2>
      <p>
        Declarative visual rule builder supporting skip logic, constraint
        checks, and cross-form edit validations.
      </p>
    </div>

    <!-- Authorization Gating check -->
    <div
      v-if="!hasEditAccess"
      class="card"
      style="
        border-left: 4px solid var(--error);
        background-color: var(--error-bg);
        padding: 24px;
      "
    >
      <div style="display: flex; gap: 16px; align-items: flex-start">
        <span style="font-size: 2rem">🚫</span>
        <div>
          <h3
            style="color: var(--error); font-weight: bold; margin-bottom: 8px"
          >
            21 CFR Part 11 Role Gating - Access Denied
          </h3>
          <p
            style="
              color: var(--neutral-dark);
              font-size: 0.95rem;
              line-height: 1.6;
            "
          >
            You do not have the required <strong>STUDY_DESIGNER</strong> role to
            view or interact with the clinical study rules. Please authenticate
            with an authorized clinical design token or consult your system
            administrator.
          </p>
        </div>
      </div>
    </div>

    <!-- Authorized Rules Panel -->
    <div
      v-else
      class="grid-2"
      style="display: grid; grid-template-columns: 1fr 1fr; gap: 24px"
    >
      <!-- Active Ruleset List -->
      <div
        class="card"
        style="display: flex; flex-direction: column; height: fit-content"
      >
        <div
          class="card-title"
          style="
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin-bottom: 16px;
          "
        >
          <span>Study Active Ruleset</span>
          <span
            v-if="loadingRules"
            style="font-size: 0.85rem; color: #64748b; font-weight: normal"
          >Loading...</span>
        </div>

        <!-- Connection Error Banner if any -->
        <div
          v-if="connectionError"
          style="
            background-color: var(--warning-bg);
            border: 1px solid var(--warning);
            color: var(--warning);
            padding: 10px;
            border-radius: 6px;
            margin-bottom: 12px;
            font-size: 0.85rem;
          "
        >
          <strong>API Mode Degraded:</strong> Running in Local Sandbox VM. Sync
          with GxP Server offline.
        </div>

        <div
          style="
            flex: 1;
            max-height: 600px;
            overflow-y: auto;
            margin-bottom: 16px;
            padding-right: 4px;
          "
        >
          <div
            v-if="activeRules.length === 0"
            style="
              color: #64748b;
              font-style: italic;
              padding: 12px 0;
              text-align: center;
            "
          >
            No active rules configured. Click "Create New Rule" to get started.
          </div>
          <div v-else>
            <div
              v-for="rule in activeRules"
              :key="rule.id"
              class="rule-card"
              style="
                border: 1px solid var(--border);
                border-radius: 8px;
                padding: 16px;
                margin-bottom: 12px;
                background-color: var(--neutral-light);
                transition: all 0.2s;
              "
            >
              <div
                style="
                  display: flex;
                  justify-content: space-between;
                  align-items: flex-start;
                  margin-bottom: 8px;
                "
              >
                <strong
                  style="
                    color: var(--accent);
                    font-size: 0.95rem;
                    font-family: monospace;
                  "
                >{{ rule.id }}</strong>
                <div style="display: flex; gap: 6px">
                  <button
                    class="btn"
                    style="
                      padding: 4px 10px;
                      font-size: 0.75rem;
                      cursor: pointer;
                      border: 1px solid var(--border);
                      border-radius: 4px;
                      background: white;
                    "
                    @click="openRuleEditor(rule)"
                  >
                    Edit
                  </button>
                  <button
                    class="btn"
                    style="
                      padding: 4px 10px;
                      font-size: 0.75rem;
                      background-color: var(--error);
                      color: white;
                      border: none;
                      border-radius: 4px;
                      cursor: pointer;
                    "
                    @click="promptDeleteRule(rule.id)"
                  >
                    Delete
                  </button>
                </div>
              </div>
              <div
                style="
                  font-size: 0.85rem;
                  color: var(--neutral-dark);
                  margin-bottom: 8px;
                  line-height: 1.4;
                "
              >
                <div style="margin-bottom: 4px">
                  <strong>Type:</strong>
                  <span
                    class="badge"
                    style="
                      background-color: var(--primary-light);
                      color: white;
                      font-size: 0.7rem;
                      padding: 2px 6px;
                    "
                  >{{ rule.type }}</span>
                </div>
                <div v-if="rule.type === 'skip_logic'">
                  <strong>Action:</strong> {{ rule.action }} field
                  <code>{{ rule.target_field }}</code>
                </div>
                <div v-else-if="rule.type === 'constraint'">
                  <strong>Target Field:</strong>
                  <code>{{ rule.target_field }}</code> <br>
                  <strong>Discrepancy Message:</strong>
                  <span style="font-style: italic; color: var(--primary)">"{{ rule.query_message }}"</span>
                </div>
                <div v-else-if="rule.type === 'cross_form_check'">
                  <strong>Discrepancy Message:</strong>
                  <span style="font-style: italic; color: var(--primary)">"{{ rule.query_message }}"</span>
                </div>
              </div>
              <div
                style="
                  font-family: monospace;
                  font-size: 0.75rem;
                  color: #0284c7;
                  background-color: #f0f9ff;
                  padding: 6px 10px;
                  border-radius: 4px;
                  border: 1px solid #e0f2fe;
                  word-break: break-all;
                "
              >
                <strong>XPath:</strong>
                {{ rule.compiled_xpath || rule.xpath || "(Not compiled)" }}
              </div>
            </div>
          </div>
        </div>

        <div style="display: flex; gap: 8px">
          <button
            class="btn btn-primary"
            style="
              width: 100%;
              display: flex;
              align-items: center;
              justify-content: center;
              gap: 8px;
            "
            @click="openRuleEditor(null)"
          >
            <span>➕</span> Create New Rule
          </button>
        </div>
      </div>

      <!-- Rules Editor Workspace -->
      <div
        v-if="showEditor"
        class="card"
        style="display: flex; flex-direction: column"
      >
        <div
          class="card-title"
          style="margin-bottom: 16px"
        >
          {{ editingRuleId ? "Edit Clinical Rule" : "Compose Clinical Rule" }}
        </div>

        <div
          style="
            display: flex;
            flex-direction: column;
            gap: 16px;
            overflow-y: auto;
            max-height: 700px;
            padding-right: 4px;
          "
        >
          <!-- Rule Type & Target definition -->
          <fieldset
            style="
              border: 1px solid var(--border);
              border-radius: 8px;
              padding: 16px;
            "
          >
            <legend
              style="padding: 0 8px; font-weight: bold; color: var(--accent)"
            >
              Rule Type & Target Definition
            </legend>

            <div
              style="
                display: grid;
                grid-template-columns: 1fr 1fr;
                gap: 16px;
                margin-bottom: 16px;
              "
            >
              <div class="form-group">
                <label
                  style="
                    display: block;
                    margin-bottom: 6px;
                    font-weight: 600;
                    font-size: 0.85rem;
                  "
                >Rule Classification Type</label>
                <select
                  v-model="ruleType"
                  style="
                    width: 100%;
                    padding: 8px;
                    border: 1px solid var(--border);
                    border-radius: 6px;
                  "
                  @change="handleTypeChange"
                >
                  <option value="skip_logic">
                    Skip Logic (Show/Hide fields)
                  </option>
                  <option value="constraint">
                    Field Constraint (Single field query validation)
                  </option>
                  <option value="cross_form_check">
                    Cross-Form / Longitudinal Check
                  </option>
                </select>
              </div>

              <div
                v-if="ruleType !== 'cross_form_check'"
                class="form-group"
              >
                <label
                  style="
                    display: block;
                    margin-bottom: 6px;
                    font-weight: 600;
                    font-size: 0.85rem;
                  "
                >Target Field</label>
                <select
                  v-model="targetField"
                  style="
                    width: 100%;
                    padding: 8px;
                    border: 1px solid var(--border);
                    border-radius: 6px;
                  "
                >
                  <option value="">
                    -- Select Target Field --
                  </option>
                  <option
                    v-for="f in mockStudyFields"
                    :key="f.id"
                    :value="f.id"
                  >
                    {{ f.name }}
                  </option>
                </select>
              </div>
            </div>

            <div
              style="display: grid; grid-template-columns: 1fr 1fr; gap: 16px"
            >
              <div
                v-if="ruleType === 'skip_logic'"
                class="form-group"
              >
                <label
                  style="
                    display: block;
                    margin-bottom: 6px;
                    font-weight: 600;
                    font-size: 0.85rem;
                  "
                >Action on Target</label>
                <select
                  v-model="ruleAction"
                  style="
                    width: 100%;
                    padding: 8px;
                    border: 1px solid var(--border);
                    border-radius: 6px;
                  "
                >
                  <option value="show">
                    Show target field
                  </option>
                  <option value="hide">
                    Hide target field
                  </option>
                </select>
              </div>

              <div
                v-if="ruleType === 'skip_logic'"
                class="form-group"
              >
                <label
                  style="
                    display: block;
                    margin-bottom: 6px;
                    font-weight: 600;
                    font-size: 0.85rem;
                  "
                >Target Form (Optional)</label>
                <select
                  v-model="targetForm"
                  style="
                    width: 100%;
                    padding: 8px;
                    border: 1px solid var(--border);
                    border-radius: 6px;
                  "
                >
                  <option value="">
                    -- Select Target Form --
                  </option>
                  <option
                    v-for="f in mockStudyForms"
                    :key="f.id"
                    :value="f.id"
                  >
                    {{ f.name }}
                  </option>
                </select>
              </div>

              <div
                v-if="ruleType !== 'skip_logic'"
                class="form-group"
                style="grid-column: span 2"
              >
                <label
                  style="
                    display: block;
                    margin-bottom: 6px;
                    font-weight: 600;
                    font-size: 0.85rem;
                  "
                >Auto-Query Discrepancy Message</label>
                <input
                  v-model="queryMessage"
                  type="text"
                  placeholder="e.g., Systolic BP is out of logical range"
                  style="
                    width: 100%;
                    padding: 8px;
                    border: 1px solid var(--border);
                    border-radius: 6px;
                  "
                >
              </div>
            </div>
          </fieldset>

          <!-- Rule Conditions -->
          <fieldset
            style="
              border: 1px solid var(--border);
              border-radius: 8px;
              padding: 16px;
            "
          >
            <legend
              style="padding: 0 8px; font-weight: bold; color: var(--accent)"
            >
              Rule Conditions (Logical Expression Tree)
            </legend>

            <div
              class="form-group"
              style="margin-bottom: 16px"
            >
              <label
                style="
                  display: block;
                  margin-bottom: 6px;
                  font-weight: 600;
                  font-size: 0.85rem;
                "
              >Match Conditions Group Operator</label>
              <select
                v-model="matchOperator"
                style="
                  padding: 6px;
                  border: 1px solid var(--border);
                  border-radius: 6px;
                  font-size: 0.85rem;
                "
              >
                <option value="and">
                  All conditions must be met (AND)
                </option>
                <option value="or">
                  Any condition can be met (OR)
                </option>
              </select>
            </div>

            <!-- Dynamic Conditions list -->
            <div style="display: flex; flex-direction: column; gap: 12px">
              <fieldset
                v-for="(cond, index) in conditions"
                :key="index"
                style="
                  border: 1px dashed var(--border);
                  border-radius: 8px;
                  padding: 12px;
                  background-color: #fafbfd;
                "
              >
                <legend
                  style="
                    font-size: 0.75rem;
                    font-weight: bold;
                    padding: 0 4px;
                    color: var(--primary);
                  "
                >
                  Condition Element #{{ index + 1 }}
                </legend>

                <div
                  style="
                    display: flex;
                    gap: 8px;
                    align-items: flex-end;
                    flex-wrap: wrap;
                  "
                >
                  <div
                    class="form-group"
                    style="flex: 1; min-width: 100px"
                  >
                    <label
                      style="
                        font-size: 0.75rem;
                        display: block;
                        margin-bottom: 4px;
                      "
                    >Left Form</label>
                    <select
                      v-model="cond.formId"
                      style="
                        width: 100%;
                        padding: 6px;
                        font-size: 0.8rem;
                        border-radius: 4px;
                        border: 1px solid var(--border);
                      "
                    >
                      <option value="">
                        -- Select Form --
                      </option>
                      <option
                        v-for="f in mockStudyForms"
                        :key="f.id"
                        :value="f.id"
                      >
                        {{ f.name }}
                      </option>
                    </select>
                  </div>

                  <div
                    class="form-group"
                    style="flex: 1; min-width: 100px"
                  >
                    <label
                      style="
                        font-size: 0.75rem;
                        display: block;
                        margin-bottom: 4px;
                      "
                    >Left Field</label>
                    <select
                      v-model="cond.fieldId"
                      style="
                        width: 100%;
                        padding: 6px;
                        font-size: 0.8rem;
                        border-radius: 4px;
                        border: 1px solid var(--border);
                      "
                    >
                      <option value="">
                        -- Select Field --
                      </option>
                      <option
                        v-for="f in mockStudyFields"
                        :key="f.id"
                        :value="f.id"
                      >
                        {{ f.name }}
                      </option>
                    </select>
                  </div>

                  <div
                    class="form-group"
                    style="flex: 1; min-width: 100px"
                  >
                    <label
                      style="
                        font-size: 0.75rem;
                        display: block;
                        margin-bottom: 4px;
                      "
                    >Operator</label>
                    <select
                      v-model="cond.operator"
                      style="
                        width: 100%;
                        padding: 6px;
                        font-size: 0.8rem;
                        border-radius: 4px;
                        border: 1px solid var(--border);
                      "
                    >
                      <option value="==">
                        equals
                      </option>
                      <option value="!=">
                        does not equal
                      </option>
                      <option value="<">
                        is less than
                      </option>
                      <option value="<=">
                        is less than or equal to
                      </option>
                      <option value=">">
                        is greater than
                      </option>
                      <option value=">=">
                        is greater than or equal to
                      </option>
                      <option value="is_empty">
                        is empty
                      </option>
                      <option value="is_not_empty">
                        is not empty
                      </option>
                    </select>
                  </div>

                  <div
                    v-if="
                      cond.operator !== 'is_empty' &&
                        cond.operator !== 'is_not_empty'
                    "
                    class="form-group"
                    style="flex: 1; min-width: 100px"
                  >
                    <label
                      style="
                        font-size: 0.75rem;
                        display: block;
                        margin-bottom: 4px;
                      "
                    >Right Value Type</label>
                    <select
                      v-model="cond.rightType"
                      style="
                        width: 100%;
                        padding: 6px;
                        font-size: 0.8rem;
                        border-radius: 4px;
                        border: 1px solid var(--border);
                      "
                    >
                      <option value="constant">
                        Constant Value
                      </option>
                      <option value="field_ref">
                        Field Reference
                      </option>
                    </select>
                  </div>

                  <div
                    v-if="
                      cond.operator !== 'is_empty' &&
                        cond.operator !== 'is_not_empty' &&
                        cond.rightType === 'constant'
                    "
                    class="form-group"
                    style="flex: 1; min-width: 100px"
                  >
                    <label
                      style="
                        font-size: 0.75rem;
                        display: block;
                        margin-bottom: 4px;
                      "
                    >Constant Value</label>
                    <input
                      v-model="cond.rightValue"
                      type="text"
                      placeholder="Value..."
                      style="
                        width: 100%;
                        padding: 6px;
                        font-size: 0.8rem;
                        border-radius: 4px;
                        border: 1px solid var(--border);
                      "
                    >
                  </div>

                  <div
                    v-if="
                      cond.operator !== 'is_empty' &&
                        cond.operator !== 'is_not_empty' &&
                        cond.rightType === 'field_ref'
                    "
                    class="form-group"
                    style="flex: 1; min-width: 100px"
                  >
                    <label
                      style="
                        font-size: 0.75rem;
                        display: block;
                        margin-bottom: 4px;
                      "
                    >Right Field</label>
                    <select
                      v-model="cond.rightFieldId"
                      style="
                        width: 100%;
                        padding: 6px;
                        font-size: 0.8rem;
                        border-radius: 4px;
                        border: 1px solid var(--border);
                      "
                    >
                      <option value="">
                        -- Select Field --
                      </option>
                      <option
                        v-for="f in mockStudyFields"
                        :key="f.id"
                        :value="f.id"
                      >
                        {{ f.name }}
                      </option>
                    </select>
                  </div>

                  <button
                    class="btn"
                    style="
                      background-color: var(--error);
                      color: white;
                      border: none;
                      padding: 6px 12px;
                      border-radius: 4px;
                      cursor: pointer;
                      font-size: 0.8rem;
                    "
                    @click="removeConditionRow(index)"
                  >
                    Remove
                  </button>
                </div>
              </fieldset>
            </div>

            <div style="margin-top: 12px">
              <button
                class="btn btn-secondary"
                style="padding: 6px 12px; font-size: 0.8rem; cursor: pointer"
                @click="addConditionRow"
              >
                ➕ Add Condition Row
              </button>
            </div>
          </fieldset>

          <!-- Live Compilation & GxP Verification Preview -->
          <fieldset
            style="
              border: 1px solid var(--border);
              border-radius: 8px;
              padding: 16px;
            "
          >
            <legend
              style="padding: 0 8px; font-weight: bold; color: var(--accent)"
            >
              Live Compilation & GxP Verification Preview
            </legend>
            <div
              style="
                background-color: #f1f5f9;
                padding: 12px;
                border-radius: 8px;
                font-family: monospace;
                font-size: 0.8rem;
                border: 1px solid var(--border);
                line-height: 1.5;
              "
            >
              <div
                style="
                  font-weight: 700;
                  color: var(--primary);
                  margin-bottom: 4px;
                "
              >
                Compiled XPath Expression:
              </div>
              <div
                style="
                  color: #0369a1;
                  margin-bottom: 12px;
                  word-break: break-all;
                  min-height: 1.5em;
                "
              >
                {{ previewXpath || "(No conditions added)" }}
              </div>

              <div
                style="
                  font-weight: 700;
                  color: var(--primary);
                  margin-bottom: 4px;
                "
              >
                Validation Feedback:
              </div>
              <div
                v-if="previewFailures.length > 0"
                style="
                  color: var(--error);
                  font-weight: 600;
                  margin-bottom: 8px;
                "
              >
                <div
                  v-for="(f, i) in previewFailures"
                  :key="i"
                >
                  ⚠️ {{ f }}
                </div>
              </div>
              <div
                v-else
                style="
                  color: var(--success);
                  font-weight: 600;
                  margin-bottom: 8px;
                "
              >
                ✅ CDASH Field & Pydantic Schema Aligned.
              </div>

              <div
                v-if="previewCircularCycles.length > 0"
                style="color: var(--error); font-weight: 600"
              >
                <div
                  v-for="(c, i) in previewCircularCycles"
                  :key="i"
                >
                  🚨 {{ c }}
                </div>
              </div>
            </div>
          </fieldset>
        </div>

        <!-- Editor Actions -->
        <div
          style="
            margin-top: 20px;
            display: flex;
            justify-content: flex-end;
            gap: 8px;
            border-top: 1px solid var(--border);
            padding-top: 16px;
          "
        >
          <button
            class="btn"
            style="padding: 8px 16px; cursor: pointer"
            @click="showEditor = false"
          >
            Cancel
          </button>
          <button
            class="btn btn-primary"
            style="padding: 8px 16px; cursor: pointer"
            @click="promptSaveRule"
          >
            Save Signed Rule
          </button>
        </div>
      </div>
    </div>

    <!-- Reason for Change Modal Dialog -->
    <div
      v-if="showReasonModal"
      class="modal-overlay"
      style="
        display: flex;
        position: fixed;
        top: 0;
        left: 0;
        right: 0;
        bottom: 0;
        background: rgba(0, 0, 0, 0.5);
        justify-content: center;
        align-items: center;
        z-index: 1000;
      "
    >
      <div
        class="modal"
        style="
          background: white;
          border-radius: 12px;
          max-width: 500px;
          width: 100%;
          padding: 24px;
          box-shadow: 0 10px 25px rgba(0, 0, 0, 0.25);
        "
      >
        <div
          class="modal-header"
          style="
            font-size: 1.2rem;
            font-weight: bold;
            color: var(--primary);
            margin-bottom: 12px;
            border-bottom: 1px solid var(--border);
            padding-bottom: 8px;
          "
        >
          Reason for Change Required
        </div>
        <div
          class="modal-body"
          style="
            font-size: 0.9rem;
            color: var(--neutral-dark);
            line-height: 1.5;
            margin-bottom: 16px;
          "
        >
          <p style="margin-bottom: 12px">
            To comply with <strong>21 CFR Part 11 / EU Annex 11</strong>, you
            must document a reason for changing this clinical data check rule.
          </p>
          <div
            class="form-group"
            style="margin-bottom: 12px"
          >
            <label style="display: block; margin-bottom: 4px; font-weight: 600">Select Standard Reason</label>
            <select
              v-model="changeReason"
              style="
                width: 100%;
                padding: 8px;
                border: 1px solid var(--border);
                border-radius: 6px;
              "
            >
              <option value="Initial Entry">
                Initial Data Entry
              </option>
              <option value="Typographical Error">
                Correction of typographical error
              </option>
              <option value="Re-measurement">
                Re-measurement of vitals
              </option>
              <option value="Transcription Error">
                Correction of transcription error
              </option>
              <option value="Protocol Amendment">
                Protocol Amendment / Update
              </option>
              <option value="Other">
                Other (specify below)
              </option>
            </select>
          </div>
          <div class="form-group">
            <label style="display: block; margin-bottom: 4px; font-weight: 600">Custom Explanation (Optional)</label>
            <textarea
              v-model="customChangeReason"
              placeholder="Explain the clinical reason for this modification..."
              style="
                width: 100%;
                height: 80px;
                padding: 8px;
                border: 1px solid var(--border);
                border-radius: 6px;
                resize: none;
              "
            />
          </div>
        </div>
        <div
          class="modal-footer"
          style="
            display: flex;
            justify-content: flex-end;
            gap: 8px;
            border-top: 1px solid var(--border);
            padding-top: 12px;
          "
        >
          <button
            class="btn"
            style="padding: 6px 16px; cursor: pointer"
            @click="closeReasonModal"
          >
            Cancel Change
          </button>
          <button
            class="btn btn-primary"
            style="padding: 6px 16px; cursor: pointer"
            @click="confirmChangeReason"
          >
            Sign & Save
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, watch } from "vue";
import { useClinicalStore } from "../stores/clinical";
import { useAuthStore } from "../stores/auth";
import { generateGatewaySignature } from "ui";

const store = useClinicalStore();
const authStore = useAuthStore();

// Base URL configuration for Rest endpoints
const GATEWAY_URL = "http://localhost:8000";

// Standard CDASH mock structures aligned with index.js legacy logic and backend visits
const mockStudyForms = [
  { id: "form_dm", name: "Demographics" },
  { id: "form_vs", name: "Vital Signs" },
  { id: "form_ae", name: "Adverse Events" },
  { id: "visit_1", name: "Visit 1" },
];

const mockStudyFields = [
  { id: "act_1", name: "Blood Draw (act_1)", formId: "visit_1" },
  { id: "act_2", name: "Vitals (act_2)", formId: "visit_1" },
  { id: "brthdt", name: "Date of Birth (brthdt)", formId: "form_dm" },
  { id: "sex", name: "Sex at Birth (sex)", formId: "form_dm" },
  { id: "vssbp", name: "Systolic BP (vssbp)", formId: "form_vs" },
  { id: "vsdpb", name: "Diastolic BP (vsdpb)", formId: "form_vs" },
  { id: "pulse", name: "Pulse Rate (pulse)", formId: "form_vs" },
  { id: "aeterm", name: "Adverse Event Term (aeterm)", formId: "form_ae" },
];

// Authorization gating
const hasEditAccess = computed(() => {
  // Check if user has study_designer role or sponsor_admin / admin privileges
  return authStore.normalizedRoles.some(
    (role) =>
      role === "study_designer" ||
      role === "sponsor_designer" ||
      role === "sponsor_admin" ||
      role === "designer" ||
      role === "admin"
  );
});

// Component state
const activeRules = ref([]);
const loadingRules = ref(false);
const connectionError = ref(false);
const showEditor = ref(false);

// Editor Form state
const editingRuleId = ref(null);
const ruleType = ref("skip_logic");
const targetField = ref("");
const ruleAction = ref("show");
const targetForm = ref("");
const queryMessage = ref("");
const matchOperator = ref("and");
const conditions = ref([]);

// Preview state
const previewXpath = ref("");
const previewFailures = ref([]);
const previewCircularCycles = ref([]);

// Reason Modal state
const showReasonModal = ref(false);
const changeReason = ref("Initial Entry");
const customChangeReason = ref("");
const pendingAction = ref(null); // { type: 'save' | 'delete', ruleId?: string, payload?: any }

// Helper to construct signed headers
async function getSignedHeaders(changeReasonText) {
  const userId = authStore.identity?.username || "fderuiter";
  // Fallback role matching backend validator logic
  const roles = authStore.isDemoMode
    ? "STUDY_DESIGNER"
    : authStore.rawRoles.join(",") || "STUDY_DESIGNER";
  const timestamp = String(Date.now() / 1000);
  const secret = "internal-gateway-secret-12345"; // pragma: allowlist secret

  const signature = await generateGatewaySignature(
    userId,
    roles,
    timestamp,
    "2",
    changeReasonText,
    secret
  );

  return {
    "Content-Type": "application/json",
    "X-User-Id": userId,
    "X-User-Roles": roles,
    "X-Gateway-Timestamp": timestamp,
    "X-Gateway-Signature": signature,
    "X-Signature-Version": "2",
    "X-Change-Reason": changeReasonText,
  };
}

// Fetch active rules from backend REST API
async function fetchRules() {
  loadingRules.value = true;
  connectionError.value = false;
  try {
    const headers = await getSignedHeaders("Fetch clinical rules");
    const response = await fetch(
      `${GATEWAY_URL}/api/v1/studies/study_1/rules`,
      { headers }
    );
    if (!response.ok) throw new Error("REST API Fetch error");
    activeRules.value = await response.json();
  } catch (err) {
    console.warn(
      "Failed to fetch active rules from REST backend, falling back to empty:",
      err
    );
    connectionError.value = true;
    activeRules.value = [];
  } finally {
    loadingRules.value = false;
  }
}

// Conditions management
function addConditionRow() {
  conditions.value.push({
    formId: "",
    fieldId: "",
    operator: "==",
    rightType: "constant",
    rightValue: "",
    rightFieldId: "",
  });
}

function removeConditionRow(index) {
  conditions.value.splice(index, 1);
  if (conditions.value.length === 0) {
    addConditionRow();
  }
}

function handleTypeChange() {
  if (ruleType.value === "skip_logic") {
    queryMessage.value = "";
    ruleAction.value = "show";
  } else {
    ruleAction.value = "show";
    targetForm.value = "";
    if (ruleType.value === "constraint" && !queryMessage.value) {
      queryMessage.value = "Value is out of range.";
    } else if (ruleType.value === "cross_form_check" && !queryMessage.value) {
      queryMessage.value = "Discrepancy detected across study forms.";
    }
  }
}

// Serialize vue forms to Pydantic Expression tree
function serializeConditions() {
  const operands = [];
  conditions.value.forEach((cond) => {
    if (!cond.fieldId) return; // Skip incomplete

    const leftRef = {
      type: "field_ref",
      field_ref: {
        field_id: cond.fieldId,
        form_id: cond.formId || null,
      },
    };

    if (cond.operator === "is_empty" || cond.operator === "is_not_empty") {
      operands.push({
        type: "function",
        operator: cond.operator,
        operands: [leftRef],
      });
    } else {
      let rightNode;
      if (cond.rightType === "constant") {
        let val = cond.rightValue;
        if (val === "true") val = true;
        else if (val === "false") val = false;
        else if (!isNaN(parseFloat(val))) val = parseFloat(val);

        rightNode = {
          type: "constant",
          value: val,
        };
      } else {
        rightNode = {
          type: "field_ref",
          field_ref: {
            field_id: cond.rightFieldId || "",
          },
        };
      }

      operands.push({
        type: "comparison",
        operator: cond.operator,
        operands: [leftRef, rightNode],
      });
    }
  });

  if (operands.length === 0) {
    return { type: "constant", value: true };
  }

  if (operands.length === 1) {
    return operands[0];
  }

  return {
    type: "logical",
    operator: matchOperator.value,
    operands: operands,
  };
}

// Dry-run preview REST API compilation
async function triggerPreview() {
  const payload = {
    type: ruleType.value,
    condition: serializeConditions(),
    target_field:
      ruleType.value !== "cross_form_check" ? targetField.value || null : null,
    target_form:
      ruleType.value === "skip_logic" ? targetForm.value || null : null,
    action: ruleType.value === "skip_logic" ? ruleAction.value : null,
    query_message:
      ruleType.value !== "skip_logic" ? queryMessage.value || null : null,
  };

  try {
    const headers = await getSignedHeaders("Rule compilation preview");
    const response = await fetch(
      `${GATEWAY_URL}/api/v1/studies/study_1/rules/preview`,
      {
        method: "POST",
        headers,
        body: JSON.stringify(payload),
      }
    );
    if (!response.ok) throw new Error("Preview API error");
    const data = await response.json();
    previewXpath.value = data.xpath;
    previewFailures.value = data.failures || [];
    previewCircularCycles.value = data.circular_cycles || [];
  } catch (err) {
    // Graceful degraded local compiling sandbox
    console.warn(
      "Dry-run preview API failed, performing fallback local compilation:",
      err
    );
    compileLocalFallback(payload);
  }
}

function compileLocalFallback(payload) {
  function compileNode(node) {
    if (!node) return "";
    if (node.type === "constant") {
      return typeof node.value === "string"
        ? `'${node.value}'`
        : String(node.value);
    }
    if (node.type === "field_ref") {
      return `/clinical_data/${node.field_ref.form_id ? node.field_ref.form_id + "/" : ""}${node.field_ref.field_id}`;
    }
    if (node.type === "function") {
      const fnName = node.operator === "is_empty" ? "empty" : "not(empty";
      const closing = node.operator === "is_not_empty" ? ")" : "";
      return `${fnName}(${compileNode(node.operands[0])})${closing}`;
    }
    if (node.type === "comparison") {
      return `(${compileNode(node.operands[0])} ${node.operator === "==" ? "=" : node.operator} ${compileNode(node.operands[1])})`;
    }
    if (node.type === "logical") {
      const ops = node.operands
        .map(compileNode)
        .join(` ${node.operator.toUpperCase()} `);
      return `(${ops})`;
    }
    return "";
  }

  previewXpath.value = compileNode(payload.condition);
  previewFailures.value = [];
  previewCircularCycles.value = [];

  // Basic circular validation
  if (payload.type === "skip_logic" && payload.target_field) {
    const refs = traverseRefs(payload.condition);
    if (refs.includes(payload.target_field)) {
      previewCircularCycles.value.push(
        `Circular dependency: ${payload.target_field} -> ${payload.target_field}`
      );
    }
  }
}

function traverseRefs(node) {
  if (!node) return [];
  if (node.type === "field_ref") return [node.field_ref.field_id];
  let refs = [];
  if (node.operands) {
    node.operands.forEach((op) => {
      refs = refs.concat(traverseRefs(op));
    });
  }
  return refs;
}

// Deserialize condition node back into local state
function deserializeNode(node) {
  if (node.type === "comparison") {
    const left = node.operands[0];
    const right = node.operands[1];
    return {
      formId: left.field_ref ? left.field_ref.form_id || "" : "",
      fieldId: left.field_ref ? left.field_ref.field_id || "" : "",
      operator: node.operator || "==",
      rightType: right.type === "field_ref" ? "field_ref" : "constant",
      rightValue: right.type === "constant" ? String(right.value) : "",
      rightFieldId:
        right.type === "field_ref" ? right.field_ref.field_id || "" : "",
    };
  } else if (node.type === "function") {
    const left = node.operands[0];
    return {
      formId: left.field_ref ? left.field_ref.form_id || "" : "",
      fieldId: left.field_ref ? left.field_ref.field_id || "" : "",
      operator: node.operator || "is_empty",
      rightType: "constant",
      rightValue: "",
      rightFieldId: "",
    };
  }
  return null;
}

function openRuleEditor(rule = null) {
  if (rule) {
    editingRuleId.value = rule.id;
    ruleType.value = rule.type;
    targetField.value = rule.target_field || "";
    ruleAction.value = rule.action || "show";
    targetForm.value = rule.target_form || "";
    queryMessage.value = rule.query_message || "";

    conditions.value = [];
    if (rule.condition) {
      const node = rule.condition;
      if (node.type === "logical" && node.operands) {
        matchOperator.value = node.operator || "and";
        node.operands.forEach((operand) => {
          const row = deserializeNode(operand);
          if (row) conditions.value.push(row);
        });
      } else {
        const row = deserializeNode(node);
        if (row) conditions.value.push(row);
      }
    }
    if (conditions.value.length === 0) {
      addConditionRow();
    }
  } else {
    editingRuleId.value = null;
    ruleType.value = "skip_logic";
    targetField.value = "";
    ruleAction.value = "show";
    targetForm.value = "";
    queryMessage.value = "";
    conditions.value = [];
    matchOperator.value = "and";
    addConditionRow();
  }
  showEditor.value = true;
  triggerPreview();
}

// GxP Change Modal Actions
function promptSaveRule() {
  if (ruleType.value !== "cross_form_check" && !targetField.value) {
    alert("Please select a target field!");
    return;
  }
  if (ruleType.value === "constraint" && !queryMessage.value) {
    alert("Please provide an auto-query message!");
    return;
  }
  if (ruleType.value === "cross_form_check" && !queryMessage.value) {
    alert("Please provide an auto-query message!");
    return;
  }

  const payload = {
    type: ruleType.value,
    condition: serializeConditions(),
    target_field:
      ruleType.value !== "cross_form_check" ? targetField.value || null : null,
    target_form:
      ruleType.value === "skip_logic" ? targetForm.value || null : null,
    action: ruleType.value === "skip_logic" ? ruleAction.value : null,
    query_message:
      ruleType.value !== "skip_logic" ? queryMessage.value || null : null,
  };

  pendingAction.value = {
    type: "save",
    payload,
  };
  changeReason.value = "Initial Entry";
  customChangeReason.value = "";
  showReasonModal.value = true;
}

function promptDeleteRule(ruleId) {
  pendingAction.value = {
    type: "delete",
    ruleId,
  };
  changeReason.value = "Protocol Amendment";
  customChangeReason.value = "";
  showReasonModal.value = true;
}

function closeReasonModal() {
  showReasonModal.value = false;
  pendingAction.value = null;
}

async function confirmChangeReason() {
  const reasonText =
    changeReason.value === "Other" && customChangeReason.value
      ? customChangeReason.value
      : changeReason.value;

  if (!reasonText || !reasonText.trim()) {
    alert(
      "Compliance modification justification reason text is strictly required!"
    );
    return;
  }

  const action = pendingAction.value;
  if (!action) return;

  try {
    if (action.type === "save") {
      const headers = await getSignedHeaders(reasonText);
      const isEdit = !!editingRuleId.value;
      const url = isEdit
        ? `${GATEWAY_URL}/api/v1/studies/study_1/rules/${editingRuleId.value}`
        : `${GATEWAY_URL}/api/v1/studies/study_1/rules`;
      const method = isEdit ? "PUT" : "POST";

      const response = await fetch(url, {
        method,
        headers,
        body: JSON.stringify(action.payload),
      });

      if (!response.ok) {
        const err = await response.json().catch(() => ({}));
        throw new Error(
          err.detail || "Server failed to compile or authorize rule."
        );
      }

      const saved = await response.json();

      // Sync verified record into compliance ledger
      await store.addLedgerBlock(
        "RULE_SAVE",
        {
          ruleId: saved.id,
          type: saved.type,
          xpath: saved.compiled_xpath || previewXpath.value,
          headers,
        },
        reasonText
      );

      alert(`Rule successfully compiled and signed save verified!`);
    } else if (action.type === "delete") {
      const headers = await getSignedHeaders(reasonText);
      const response = await fetch(
        `${GATEWAY_URL}/api/v1/studies/study_1/rules/${action.ruleId}`,
        {
          method: "DELETE",
          headers,
        }
      );

      if (!response.ok) {
        const err = await response.json().catch(() => ({}));
        throw new Error(err.detail || "Server failed to delete rule.");
      }

      // Sync deletion block
      await store.addLedgerBlock(
        "RULE_DELETE",
        {
          ruleId: action.ruleId,
          headers,
        },
        reasonText
      );

      alert("Rule successfully soft-deleted!");
    }

    showEditor.value = false;
    showReasonModal.value = false;
    pendingAction.value = null;
    await fetchRules();
  } catch (err) {
    alert(`GxP Transaction Aborted: ${err.message}`);
  }
}

// Dynamic watchers to trigger live preview
watch(
  () => conditions.value,
  () => {
    triggerPreview();
  },
  { deep: true }
);

watch(
  [ruleType, targetField, ruleAction, targetForm, queryMessage, matchOperator],
  () => {
    triggerPreview();
  }
);

onMounted(async () => {
  if (hasEditAccess.value) {
    await fetchRules();
  }
});
</script>

<style scoped>
.rule-card:hover {
  border-color: var(--accent) !important;
  box-shadow: 0 4px 6px -1px rgb(0 0 0 / 0.05);
}
</style>
