# Stage 02 - Containers

Status: API and automated checks complete. Stage 03 not started.
Manual scene/runtime integration verification remains outstanding.

## Files and contracts

- ContainerDefinition: immutable plain C# type metadata with authored stable UUID.
- InventoryContainer: independent inventory, read-only ParentId and local
  IsAvailable. ContainerId equals the owned inventory identity.
- InventoryComponent: explicit runtime Bind/Unbind; OnDestroy detaches without
  deleting domain data. Domain state is excluded from scene serialization.
- ContainerService: trusted single-threaded owner of container registry, item
  catalog, Add/Remove/Transfer, availability, deletion and parent relationships.
- InventoryError: explicit cycle, child-container and invalid-reference failures.
- Tests/Items: actual Core/Containers sources, with transfer and hierarchy checks.

## Behavior

Full Transfer preserves instance ID; partial Transfer creates a collision-checked
ID for the destination portion. No implicit merging. Validation and snapshots
precede state mutation. Changed observers see both sides committed. Quantity and
properties are conserved; observer exceptions do not undo successful operations.

Nested storage is a tree of containers, with at most one parent and explicit
TrySetParent(actor, childId, parentId); null detaches. Self and indirect cycles,
unknown references and broken internal chains fail closed. Traversal is iterative.
Reparent checks child, old-parent Unnest, new-parent Nest, and ancestor policies.
Content operations check the requested action and availability on every ancestor.
IsAvailable itself describes local state; ancestor availability is checked by the
service. Idempotent parenting emits no event. HierarchyChanged reports immutable
before/after parent IDs and deletion; mutations from policies/events are blocked.

Delete is a trusted administrative operation. It rejects nonempty storage and
parents with children. Deleting an empty leaf clears its parent reference and
availability, removes it from registry/child queries, and emits a hierarchy event.
No recursive deletion or implicit loss of contents. Old membership snapshots stay
stable; container objects within snapshots remain live read-only views.

The access callback is mandatory. Actor identity must come from authenticated
application context. Create, register, availability and delete are trusted admin
calls, not client request endpoints. No threading or multiplayer guarantees yet.

Container nesting is independent of scene parenting and item quantities. It does
not associate a bag ItemInstance with a child container yet. Item Transfer does not
move a subtree; container reparenting preserves its contents and descendants.
Weight, capacity, slots, stacking, filtering and persistence are later-stage work.

## Verification

- dotnet build Editor/inventorymodule.editor.csproj --no-restore:
  game/editor builds passed, zero warnings and errors.
- dotnet run --project Tests/Items/Items.Tests.csproj:
  35 test groups passed (16 core, 4 foundations, 7 transfer, 8 nesting).
- Tests include full/partial/replayed transfer, failure atomicity, access rejection,
  int.MaxValue and long totals, deletion, direct/indirect cycles, missing references,
  reparent/detach and both-parent access, inherited access/availability, stale
  references, deliberate internal corruption, a 128-level tree and event isolation.
- No scene/editor runtime, engine whitelist, resource round-trip or networking
  checks performed. Component lifecycle API was previously verified against the
  installed engine and official Component Methods documentation.

Manual scene check: attach InventoryComponent to a GameObject; bind retained state;
verify null/replacement rejection, idempotence, detach/rebind and destruction
without data deletion. Do not infer runtime validation from successful builds.

Roadmap updated with completed stage 02 API/checklist and explicit manual limitation.
No stage 03 changes, commits, publication or generated-project modifications.
Workspace has no Git metadata; commit history and Git diff unavailable.
