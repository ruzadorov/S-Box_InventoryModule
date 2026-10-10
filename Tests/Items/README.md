# Item foundation checks

Run `dotnet run --project Tests/Items/Items.Tests.csproj` from the project root.
This dependency-free executable tests the actual ItemInstance, Core and Containers sources and exits
nonzero on failure. It does not substitute or mock any s&box API.

Compile the engine integration with `dotnet build Code/inventorymodule.csproj --no-restore`.
ItemDefinition uses the installed engine's GameResource and AssetType APIs.
The generated s&box project files are unchanged.

Manual editor check still required: create an Inventory Item asset, assign a
non-empty UUID in D format to DefinitionId, save, reload, rename/move the asset,
and verify the ID remains unchanged. Copying an asset requires assigning a new
ID; duplicate definition IDs are rejected by InventoryService registration.

ItemInstance is a snapshot, not a public mutation API. Its constructor validates
IDs, positive quantities and properties; inventory operations return typed
results and enforce ownership and uniqueness. Passing the same ID to two snapshot
constructors is allowed; neither snapshot is yet owned by an inventory.

Basic API on the owning game thread:

```csharp
var service = new SBox.Inventory.InventoryService();
var typeId = Guid.NewGuid(); // In games, use an authored ItemDefinition ID.
service.TryRegisterDefinition(typeId);
var added = service.TryAdd(typeId, 3);
if (added.Success)
{
    var count = service.Inventory.Count(typeId); // 3 (long)
    var removed = service.TryRemove(added.Item.InstanceId, 1);
    var current = service.Inventory.Find(added.Item.InstanceId); // Amount == 2
}
```

For engine resources, use the ItemDefinitionRegistration extension overload.
Changed observers see committed state; inspect NotificationErrors for observer
exceptions. Reentrant mutations are rejected. This is a single-threaded core,
not a network boundary; authority/access policies belong to later adapters.

Container API (trusted authority; actor comes from authenticated game context):

```csharp
var actor = Guid.NewGuid();
var containers = new ContainerService((who, container, action) => who == actor);
containers.TryRegisterDefinition(typeId);
var definition = new ContainerDefinition(Guid.NewGuid(), "Storage");
var source = containers.Create(definition);
var target = containers.Create(definition);
var item = containers.TryAdd(actor, source.ContainerId, typeId, 3);
var move = containers.TryTransfer(actor, source.ContainerId, target.ContainerId,
    item.Item.InstanceId, 1);
```

Supply a real game access policy. Creation, registration, availability changes and
deletion are trusted administrative calls. Transfer never merges instances; a full
move preserves identity and a partial move creates a distinct identity. Delete
rejects nonempty storage. Changed emits one event describing both transfer sides.

Nesting is an explicit tree of containers, independent of item quantities and scene
parents. `TrySetParent(actor, childId, parentId)` attaches/reparents; passing null
detaches to root. ParentId is read-only; GetChildren returns a membership snapshot.
Each operation checks availability and the requested action against every ancestor.
Reparent also requires Unnest on the old parent and Nest on the new parent.
No-op parenting emits no event. HierarchyChanged reports committed before/after
parent IDs; deletion sets Deleted and clears the parent reference. Delete rejects
containers with children. This model does not yet associate a bag ItemInstance
with its internal container, and moving an item does not move a container subtree.

Rules are configured per container through the trusted service:

```csharp
containers.TrySetRules(target.ContainerId, new IInventoryRule[]
{
    new SlotLimitRule(8),
    new QuantityLimitRule(100)
});
```

Check the returned result: existing contents must satisfy the proposed rules.
One instance is one slot; quantities count units of every type. Limits apply only
to direct contents. Validation runs before Add/Remove/Transfer commits, source and
target included; exceptions reject the operation. Custom rules must be pure and
have immutable configuration. Rule-list membership is copied, not rule objects.

Weight rule example (consistent game-defined units, such as kg):

```csharp
var weightRule = new WeightLimitRule(10m,
    new Dictionary<Guid, decimal> { [typeId] = 0.25m },
    reductionFraction: 0.5m);
containers.TrySetRules(target.ContainerId, new IInventoryRule[] { weightRule });
var error = weightRule.TryMeasure(target.Inventory.Items, out var raw, out var carried);
```

Check both operation and measurement results. Capacity uses raw direct-content
weight; reduction affects carried weight only. Unknown IDs fail rather than count
as zero. The catalog is copied; to change weights, replace the rule and revalidate
existing contents. This does not aggregate children or include empty-container weight.

Type/category filters compose with all other rules:

```csharp
containers.TrySetRules(target.ContainerId, new IInventoryRule[]
{
    new AllowedItemTypesRule(new[] { typeId }),
    new AllowedItemCategoriesRule(new[] { "supplies" },
        new Dictionary<Guid, string> { [typeId] = "supplies" }),
    weightRule
});
```

Category IDs use ordinal case-sensitive identity; one category per item type.
Unknown categories reject contents. Empty allowlists allow only empty inventories.
Configuration is copied; changing input collections does not change active rules.

Stack limits and explicit merge:

```csharp
containers.TrySetRules(target.ContainerId, new IInventoryRule[]
{
    new StackLimitRule(new Dictionary<Guid, int> { [typeId] = 20 }),
    weightRule
});
var merged = containers.TryMergeStacks(actor, source.ContainerId, target.ContainerId,
    sourceItemId, targetItemId, amount: 3);
```

Types and complete ordinal property dictionaries must match. Destination ID survives;
a fully consumed source ID is removed. Merge result Item is the resulting destination
snapshot. Within one container both changes are validated as one final projection.
Changed describes both source and destination; their container IDs can be equal.
Maximum one means non-stackable. Unknown stack limits reject contents. Add does not
implicitly merge or split oversized amounts. Partial Transfer separates quantities
into another container; explicit merge can recombine them. No partial success.
