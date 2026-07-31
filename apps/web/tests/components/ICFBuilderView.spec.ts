import { describe, it, expect, beforeEach } from 'vitest';
import { mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { useEconsentStore } from '../../src/stores/econsent';
import ICFBuilderView from '../../src/views/ICFBuilderView.vue';

describe('ICFBuilderView.vue and Pinia Store Unit Tests', () => {
  beforeEach(() => {
    const pinia = createPinia();
    setActivePinia(pinia);
  });

  it('adds a new consent section and updates the reactive store', async () => {
    const store = useEconsentStore();
    const wrapper = mount(ICFBuilderView);

    // Store is initialized on mount
    expect(store.sections.length).toBeGreaterThan(0);
    const initialCount = store.sections.length;

    // Enter a new section title in the sidebar input
    const input = wrapper.find('input[placeholder="New Section Title..."]');
    expect(input.exists()).toBe(true);
    await input.setValue('Genetic Screening and Biobanking');

    // Click the Add Section button
    const addButton = wrapper.find('.add-section-form button');
    expect(addButton.exists()).toBe(true);
    await addButton.trigger('click');

    // Verify the store is updated reactively
    expect(store.sections.length).toBe(initialCount + 1);
    const addedSection = store.sections[store.sections.length - 1];
    expect(addedSection.title).toBe('Genetic Screening and Biobanking');

    // Verify it is highlighted as the active section in the workspace
    expect(wrapper.text()).toContain('Genetic Screening and Biobanking');
  });

  it('prompts for change justification and increments the version index when publishing (v1.0 -> v2.0)', async () => {
    const store = useEconsentStore();
    const wrapper = mount(ICFBuilderView);

    // Initial version
    expect(store.currentIcf?.version).toBe('v1.0');

    // Verify publish button exists and click it
    const publishBtn = wrapper.find('.btn-publish');
    expect(publishBtn.exists()).toBe(true);
    await publishBtn.trigger('click');

    // Modal is now shown
    expect(wrapper.find('.publish-modal-overlay').exists()).toBe(true);

    // Try to publish without a reason - should show validation error
    const confirmBtn = wrapper.find('.modal-footer .btn-primary');
    expect(confirmBtn.exists()).toBe(true);
    await confirmBtn.trigger('click');

    // Should stay open and show error
    expect(wrapper.find('.publish-modal-overlay').exists()).toBe(true);
    expect(wrapper.find('.error-text').text()).toContain('Justification is mandatory');

    // Enter change justification
    const textarea = wrapper.find('#publish-reason');
    expect(textarea.exists()).toBe(true);
    await textarea.setValue('Protocol amendment update with genetic screening disclaimers');

    // Confirm and publish
    await confirmBtn.trigger('click');

    // Modal is closed
    expect(wrapper.find('.publish-modal-overlay').exists()).toBe(false);

    // Version in store is incremented
    expect(store.currentIcf?.version).toBe('v2.0');

    // Version audit history contains the record
    expect(store.versionHistory.length).toBeGreaterThan(1);
    const latestHistory = store.versionHistory[store.versionHistory.length - 1];
    expect(latestHistory.version).toBe('v2.0');
    expect(latestHistory.reason).toBe('Protocol amendment update with genetic screening disclaimers');
  });
});
