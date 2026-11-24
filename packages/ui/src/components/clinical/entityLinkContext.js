import { provide } from "vue";

export const ENTITY_LINK_RESOLVER_KEY = Symbol.for(
  "cadence.ui.entityLinkResolver"
);

export function provideEntityLinkResolver(resolver) {
  provide(ENTITY_LINK_RESOLVER_KEY, resolver);
}
