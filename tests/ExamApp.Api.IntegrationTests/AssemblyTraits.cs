// #416: whole project needs Docker/Testcontainers; CI's unit job excludes it with
// `--filter "Category!=Integration"` (xunit v3 TraitAttribute supports assembly scope).
[assembly: Trait("Category", "Integration")]
