using System;

namespace SBox.Inventory;

/// <summary>
/// Scene-independent container state. Each container exclusively owns its inventory.
/// Lifecycle, access, nesting and transfer will be controlled by the container service.
/// </summary>
public sealed class InventoryContainer
{
	internal InventoryService Operations { get; }
	private bool _available = true;
	private Guid? _parentId;
	private InventoryRuleSet _rules = InventoryRuleSet.Empty;
	public InventoryRuleSet Rules => _rules;
	internal void SetRules( InventoryRuleSet rules ) => _rules = rules;
	/// <summary>Service-controlled parent container, or null for a root.</summary>
	public Guid? ParentId => _parentId;
	internal void SetParent( Guid? parentId ) => _parentId = parentId;
	public bool IsAvailable => _available;
	internal void SetAvailable( bool available ) => _available = available;
	/// <summary>Runtime container identity, equal to the identity of its owned inventory.</summary>
	public Guid ContainerId => Inventory.InventoryId;
	public ContainerDefinition Definition { get; }
	/// <summary>Read-only contents. No mutable service is exposed to callers.</summary>
	public Inventory Inventory => Operations.Inventory;

	public InventoryContainer( ContainerDefinition definition ) : this( definition, Guid.NewGuid ) { }
	internal InventoryContainer( ContainerDefinition definition, Func<Guid> createId )
	{
		Definition = definition ?? throw new ArgumentNullException( nameof( definition ) );
		Operations = new InventoryService( createId );
		Operations.ValidateChange = change => Rules.Validate( new InventoryRuleContext( Inventory, change ) );
	}
}
