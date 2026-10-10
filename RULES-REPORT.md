# Stage 03 - Inventory Rules

Status: API and automated checks complete. Stage 04 not started.
Manual engine runtime and scene integration checks remain outstanding.

## Implementation

- Code/Core/InventoryValidation.cs: IInventoryRule, immutable Before/After membership
  snapshots and ordered InventoryRuleSet. Core owns the extension contract and
  never depends on concrete Rules classes. Projection supports both sides of a
  same-container merge so units are not double counted.
- Code/Rules/SlotLimitRule.cs: one instance record occupies one slot.
- Code/Rules/QuantityLimitRule.cs: total units, long maximum, safe subtraction.
- Code/Rules/WeightLimitRule.cs: copied Guid-to-decimal weights; raw direct-content
  capacity, reduction in [0,1] affects carried weight only. Unknown weights and
  arithmetic overflow fail closed. TryMeasure returns explicit errors and clears
  outputs on failure. Units are chosen consistently by the game.
- Code/Rules/AllowedItemTypesRule.cs: stable UUID allowlist.
- Code/Rules/AllowedItemCategoriesRule.cs: copied one-category-per-type catalog,
  ordinal case-sensitive IDs. Missing metadata fails closed. Empty allowlists
  allow empty contents only. Duplicate allowed entries collapse.
- Code/Rules/StackLimitRule.cs: copied positive per-type maximums. One means
  non-stackable. Unknown type limits fail closed.
- ContainerService.TrySetRules: trusted per-container configuration with defensive
  list copying and validation of existing contents before assignment.
- ContainerService.TryMergeStacks: explicit atomic merge within one container or
  across two. Complete type and ordinal property equality required. Target ID
  survives, source remainder retains ID; full consumption removes source ID.
  Result Item is the resulting target snapshot. Quantity overflow is rejected.
- Core Add/Remove and container Transfer/Merge validate before mutation. Both sides
  and source restrictions are checked. Ordered rules reject on first failure;
  exceptions become RuleFailed. Reentrant mutations from rules/events are blocked.

## Semantics and limits

No automatic merge or bulk splitting. Oversized Add is rejected. Partial Transfer
separates a portion into another container and explicit merge recombines compatible
portions. No partial success. Changed events expose both committed snapshots;
for same-container merge ContainerId equals TargetId. Observer failures are separate
from the successful mutation result.

Rule membership and built-in configurations are immutable. Custom rules must be
pure with immutable configuration: rule objects themselves are not cloned.
Removing all rules restores unrestricted contents. Zero slot/quantity/weight limits
are valid. Stack maximums must be positive.

All rules count direct contents only. Nested weight, container shell weight and
parent capacity aggregation are not implemented. Reduction does not bypass raw
capacity. Category/weight/stack metadata is provided by the game; ItemDefinition
assets were unchanged. Runtime resource integration is not verified by these tests.

## Verification

- dotnet run --project Tests/Items/Items.Tests.csproj: 62 groups passed.
- dotnet build Editor/inventorymodule.editor.csproj --no-restore: game/editor builds
  passed, zero warnings and errors, against installed engine DLLs/generators.
- Tests cover limits, immutable configuration, unknown metadata, invalid input,
  Add/Remove/Transfer/Merge atomicity, rule replacement, source rejection, property
  compatibility, same-container projections, int/decimal overflow, zero limits,
  full/partial merges, identity/quantity conservation, composition of all built-in
  rules and observer/reentrancy behavior. Earlier Core/Containers checks also pass.
- No editor runtime, engine whitelist, resource round-trip, network or multithread
  checks performed. Manual scene check from stage 02 remains outstanding.

Roadmap updated for completed stage 03 tasks with the direct-content limitation.
No stage 04 changes, commits, publication or generated-project modifications.
Workspace has no Git metadata; Git diff and history are unavailable.
