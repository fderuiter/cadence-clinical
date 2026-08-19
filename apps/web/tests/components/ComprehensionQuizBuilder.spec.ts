import { describe, it, expect, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { useEconsentStore } from "../../src/stores/econsent";
import ComprehensionQuizBuilder from "../../src/components/econsent/ComprehensionQuizBuilder.vue";

describe("ComprehensionQuizBuilder.vue Component Tests", () => {
  beforeEach(() => {
    const pinia = createPinia();
    setActivePinia(pinia);
  });

  it("renders authoring mode by default and allows adding/removing questions", async () => {
    const store = useEconsentStore();
    store.loadIcf();

    const wrapper = mount(ComprehensionQuizBuilder);

    expect(wrapper.text()).toContain("Comprehension Check & Quiz Assessment");
    expect(wrapper.find(".authoring-view").exists()).toBe(true);

    const initialCount = store.quizQuestions.length;
    expect(initialCount).toBeGreaterThan(0);

    // Add question
    const addBtn = wrapper.find(".btn-add-question");
    expect(addBtn.exists()).toBe(true);
    await addBtn.trigger("click");

    expect(store.quizQuestions.length).toBe(initialCount + 1);

    // Remove last question
    const deleteBtns = wrapper.findAll(".btn-delete");
    await deleteBtns[deleteBtns.length - 1].trigger("click");
    expect(store.quizQuestions.length).toBe(initialCount);
  });

  it("switches to interactive assessment mode and presents questions with accessible options", async () => {
    const store = useEconsentStore();
    store.loadIcf();

    const wrapper = mount(ComprehensionQuizBuilder);

    // Switch to interactive assessment mode
    const modeTabs = wrapper.findAll(".mode-tab-btn");
    expect(modeTabs.length).toBe(2);

    await modeTabs[1].trigger("click"); // Click Interactive Assessment
    expect(wrapper.find(".interactive-view").exists()).toBe(true);

    const questionCards = wrapper.findAll(".assessment-question-card");
    expect(questionCards.length).toBe(store.quizQuestions.length);
    expect(wrapper.text()).toContain("Question #1:");
  });

  it("provides instant feedback and enforces passing score threshold (passing case)", async () => {
    const store = useEconsentStore();
    store.loadIcf();
    store.passingThreshold = 80;

    const wrapper = mount(ComprehensionQuizBuilder);

    // Switch to interactive mode
    const modeTabs = wrapper.findAll(".mode-tab-btn");
    await modeTabs[1].trigger("click");

    // Select correct answers for both default questions (both have correctAnswerIndex = 1)
    const q1 = store.quizQuestions[0];
    const q2 = store.quizQuestions[1];

    wrapper.vm.selectAnswer(q1.id, q1.correctAnswerIndex);
    wrapper.vm.selectAnswer(q2.id, q2.correctAnswerIndex);

    // Submit / Check answers
    await wrapper.find(".btn-submit-answers").trigger("click");

    // Result banner is displayed
    const banner = wrapper.find("#assessment-result-banner");
    expect(banner.exists()).toBe(true);
    expect(banner.classes()).toContain("passed");
    expect(wrapper.text()).toContain("Passing Threshold Met!");
    expect(wrapper.text()).toContain("Score: 100%");
    expect(store.quizPassed).toBe(true);

    // Proceed to signature button is visible and clickable
    const proceedBtn = wrapper.find("#btn-proceed-signature");
    expect(proceedBtn.exists()).toBe(true);

    await proceedBtn.trigger("click");
    expect(wrapper.emitted("proceed-to-sign")).toBeTruthy();
  });

  it("enforces passing threshold and presents instant hints on incorrect answers (failing case)", async () => {
    const store = useEconsentStore();
    store.loadIcf();
    store.passingThreshold = 80;

    const wrapper = mount(ComprehensionQuizBuilder);

    // Switch to interactive mode
    const modeTabs = wrapper.findAll(".mode-tab-btn");
    await modeTabs[1].trigger("click");

    const q1 = store.quizQuestions[0];
    const q2 = store.quizQuestions[1];

    // Select incorrect answer for q1 (index 0 instead of 1) and correct for q2
    wrapper.vm.selectAnswer(q1.id, 0);
    wrapper.vm.selectAnswer(q2.id, q2.correctAnswerIndex);

    await wrapper.find(".btn-submit-answers").trigger("click");

    const banner = wrapper.find("#assessment-result-banner");
    expect(banner.exists()).toBe(true);
    expect(banner.classes()).toContain("failed");
    expect(wrapper.text()).toContain("Passing Threshold Not Met");
    expect(wrapper.text()).toContain("Score: 50%");
    expect(store.quizPassed).toBe(false);

    // Feedback hint is displayed for incorrect question
    const hintBox = wrapper.find(".instant-hint-box");
    expect(hintBox.exists()).toBe(true);
    expect(hintBox.text()).toContain("Feedback Hint");
    expect(hintBox.text()).toContain(q1.hint);

    // Proceed to signature button is NOT shown when failed
    expect(wrapper.find("#btn-proceed-signature").exists()).toBe(false);

    // Reset answers
    await wrapper.find(".btn-reset-quiz").trigger("click");
    expect(wrapper.find("#assessment-result-banner").exists()).toBe(false);
    expect(store.quizPassed).toBe(false);
  });
});
