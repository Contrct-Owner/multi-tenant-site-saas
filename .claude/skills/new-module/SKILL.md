---
name: new-module
description: Scaffold a new vertical-slice module with its own schema, DbContext, migration history, Wolverine registration, and test fixtures. Use whenever adding a module, domain slice, or bounded context to the backend.
---

# New module

Every module is a vertical slice owning its own Postgres schema and DbContext
(ADR 17). Never hand-roll one - missing a step here is the main way module
boundaries erode.

## If the module generator exists

Run it and stop - it performs the checklist below:

```bash
dotnet run --project tools/ModuleGenerator -- --name <ModuleName>
```

## Until the generator exists, perform every step

1. **Project layout**: `modules/<ModuleName>/` with feature folders (one folder
   per use case), not layer folders. Handlers are Wolverine handlers.
2. **Schema + DbContext**: new `<ModuleName>DbContext` with
   `HasDefaultSchema("<module_name>")` and its own `__EFMigrationsHistory` in
   that schema. Register the connection through the region-resolved context
   factory - never a raw connection string (ADR 35).
3. **First migration**: create the schema; every tenant-scoped table gets an RLS
   policy in the same migration (use the new-migration skill).
4. **Wolverine registration**: register the module's handlers and any
   integration message subscriptions. Cross-module communication is messages +
   outbox only - no project reference to another module's internals.
5. **Contracts**: anything other modules may consume goes in
   `shared/contracts/<ModuleName>/` (DTOs and integration events only).
6. **Architecture tests**: add the module to the boundary rules (may not
   reference other modules' internals; must only expose contracts).
7. **Test fixtures**: per-module fixture that provisions the schema, applies
   migrations, and sets tenant context. Add the module's endpoints to the
   tenant-isolation golden suite (replay as tenant B against tenant A's ids,
   assert 404).
8. **Entity checklist**, for every new entity:
   - deletion tier declared (ADR 25)
   - temporal columns typed as one of the four kinds (ADR 26/27)
   - UUIDv7 keys (ADR 35)
   - fact tables stamp hierarchy path + business date (ADR 2/26)
