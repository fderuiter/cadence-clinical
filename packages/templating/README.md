# Cadence.Templating

Liquid template rendering (`ITemplateRenderer`, `FluidTemplateRenderer`) for application modules such as CTMS
letters, notifications, safety narratives and DDF artifact generation.

Rendering is a non-goal of the Clinical Kernel (`docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md` §0.2), so this
package lives outside `packages/kernel` and must not be referenced by `Cadence.Kernel.*` projects.

Register with `services.AddFluidTemplating()`.
