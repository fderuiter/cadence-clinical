using Xunit;

// Every test in this assembly needs a real PostgreSQL database (docs/TESTING.md). The trait lets
// `dotnet test --solution Cadence.sln --filter "Category!=Integration" --ignore-exit-code 8` leave them out.
[assembly: Trait("Category", "Integration")]
