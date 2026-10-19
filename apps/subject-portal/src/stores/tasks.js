// Assigned questionnaires, their instrument definitions and the questionnaire
// being filled in.
import { defineStore } from "pinia";

export const useTasksStore = defineStore("tasks", {
  state: () => ({
    assignments: [],
    assignmentsError: false,
    instruments: {}, // Map keyed by instrument id
    tasksLoading: false,
    tasksError: null,
    instrumentsLoading: false,
    instrumentsError: null,
    activeQuestionnaire: null,
  }),
});
