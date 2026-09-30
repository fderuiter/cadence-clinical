using Xunit;

// Every test in this assembly needs a real PostgreSQL database (docs/TESTING.md). The trait lets
// `dotnet test Cadence.sln --filter "Category!=Integration"` leave them out.
[assembly: Trait("Category", "Integration")]
