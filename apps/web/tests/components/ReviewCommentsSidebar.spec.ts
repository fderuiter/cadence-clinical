import { describe, it, expect } from "vitest";
import { mount } from "@vue/test-utils";
import ReviewCommentsSidebar from "../../src/components/crf/ReviewCommentsSidebar.vue";

describe("ReviewCommentsSidebar.vue", () => {
  const sampleComments = [
    {
      id: "comment-1",
      field_id: "field-age",
      authorName: "John Doe",
      text: "Please verify age range constraints",
      isResolved: false,
      createdAt: "2026-08-30T10:00:00.000Z",
    },
    {
      id: "comment-2",
      field_id: "field-age",
      authorName: "Jane Smith",
      text: "Validated and verified as correct",
      isResolved: true,
      createdAt: "2026-08-30T11:00:00.000Z",
    },
    {
      id: "comment-3",
      field_id: "field-gender",
      authorName: "John Doe",
      text: "Check gender options mapping",
      isResolved: false,
      createdAt: "2026-08-30T12:00:00.000Z",
    },
  ];

  it("filters comments by selected fieldId and displays them correctly", () => {
    const wrapper = mount(ReviewCommentsSidebar, {
      props: {
        comments: sampleComments,
        fieldId: "field-age",
      },
    });

    const cards = wrapper.findAll(".comment-card");
    expect(cards.length).toBe(2);

    expect(wrapper.find(".sidebar-header h3").text()).toBe("Review Comments (2)");
    expect(wrapper.find(".field-indicator").text()).toContain("field-age");

    const texts = cards.map((c) => c.find(".comment-text").text());
    expect(texts).toContain("Please verify age range constraints");
    expect(texts).toContain("Validated and verified as correct");

    // The resolved class is set on resolved comment cards
    const resolvedCard = wrapper.find(".comment-card.resolved");
    expect(resolvedCard.exists()).toBe(true);
    expect(resolvedCard.find(".comment-text").text()).toBe("Validated and verified as correct");
  });

  it("displays empty state when no comments match the selected fieldId", () => {
    const wrapper = mount(ReviewCommentsSidebar, {
      props: {
        comments: sampleComments,
        fieldId: "field-weight",
      },
    });

    expect(wrapper.find(".sidebar-header h3").text()).toBe("Review Comments (0)");
    expect(wrapper.find(".empty-state").exists()).toBe(true);
    expect(wrapper.find(".empty-state").text()).toContain("No comments yet for this field");
  });

  it("emits post-comment event with correct payload when post button is clicked", async () => {
    const wrapper = mount(ReviewCommentsSidebar, {
      props: {
        comments: sampleComments,
        fieldId: "field-age",
      },
    });

    const textarea = wrapper.find("textarea");
    await textarea.setValue("This is a new test comment");

    await wrapper.find(".btn-primary").trigger("click");

    expect(wrapper.emitted("post-comment")).toBeTruthy();
    expect(wrapper.emitted("post-comment")[0]).toEqual([
      { fieldId: "field-age", text: "This is a new test comment" },
    ]);

    // Textarea is cleared after successful emit/submission
    expect(textarea.element.value).toBe("");
  });

  it("emits resolve event when clicking on Resolve button", async () => {
    const wrapper = mount(ReviewCommentsSidebar, {
      props: {
        comments: sampleComments,
        fieldId: "field-age",
      },
    });

    // Locate the Resolve button (only 1 open comment, so 1 Resolve button is visible)
    const resolveBtn = wrapper.find("button.btn-sm");
    expect(resolveBtn.exists()).toBe(true);

    await resolveBtn.trigger("click");

    expect(wrapper.emitted("resolve")).toBeTruthy();
    expect(wrapper.emitted("resolve")[0]).toEqual(["comment-1"]);
  });
});
