import ParticipantsSection from "../../components/canvas/ParticipantsSection.vue";
import type { CanvasSectionDescriptor } from "./types";

const participantsSection: CanvasSectionDescriptor = {
  id: "participants",
  stageId: "conduct",
  order: 10,
  label: "Participants",
  description: "The study's participants in enrollment order",
  focusTargets: ["#participants-title"],
  loadOnEnter: (ctx) => {
    const filter = {
      status: ctx.statusFilter,
      siteId: ctx.siteFilter,
      q: ctx.participantSearchFilter,
    };
    const loaded = ctx.canvas.participantsFilter;
    const sameFilter =
      (loaded.status ?? "") === filter.status &&
      (loaded.siteId ?? "") === filter.siteId &&
      (loaded.q ?? "") === filter.q;
    const hasPage =
      ctx.canvas.participants.length > 0 || ctx.canvas.participantsTotal > 0;
    if (!sameFilter || (!hasPage && !ctx.canvas.participantsLoading)) {
      ctx.canvas.loadParticipants({
        status: filter.status || undefined,
        siteId: filter.siteId || undefined,
        q: filter.q || undefined,
      });
    }
  },
  resolveRender: (ctx) => ({
    component: ParticipantsSection,
    props: {
      status: ctx.statusFilter,
      siteId: ctx.siteFilter,
      query: ctx.participantSearchFilter,
      selectedId: ctx.selectedParticipantOrRecordId,
      canManageParticipants: ctx.canChangeParticipants,
    },
    listeners: {
      select: ctx.onSelectParticipant as (...args: never[]) => unknown,
      filter: ctx.onApplyParticipantFilter as (...args: never[]) => unknown,
    },
  }),
};

export default participantsSection;
