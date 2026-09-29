# Templating Package

## Overview

Sandboxed Liquid (Fluid) rendering of a module's own embedded templates. Used by CTMS letters,
notification bodies, safety ICSR XML and the DDF compiler's ODM/OpenRosa output.

---

## Architectural Boundaries

- Lives outside the kernel: rendering is a kernel non-goal (Clinical Kernel spec §0), and
  `Cadence.Kernel.Abstractions` is limited to IDs, value objects, commands/events and ports (§9).
- A module declares a `TemplateCatalog`: a closed allow-list mapping public keys to templates embedded in
  that module's own assembly. Callers name a key, never a resource; no other assembly is searched.
- Parsed templates are cached per owning assembly and resource, so two modules can use the same key or
  file name without colliding.

---

## Public APIs / Exports

- `TemplateCatalog`: per-module allow-list; resolves every template at construction and throws if one is
  missing or ambiguous.
- `ITemplateRenderer.RenderAsync(catalog, key, model)` / `Render(...)`: throws
  `TemplateNotFoundException` for a key outside the catalog.
- `FluidTemplateRenderer`: the Fluid implementation; `AddFluidTemplating()` registers it in DI.

```csharp
public static TemplateCatalog Catalog { get; } = new(
    typeof(MyEndpoints).Assembly,
    new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["confirmation_letter"] = "confirmation_letter.liquid",
    });

if (!Catalog.Contains(templateName)) { /* 400 ProblemDetails */ }
string text = await renderer.RenderAsync(Catalog, templateName, model);
```

---

## Dependencies

- `Fluid.Core`
- `Microsoft.Extensions.DependencyInjection.Abstractions`

---

## Tests

```bash
dotnet test tests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~TemplateCatalogTests"
```
