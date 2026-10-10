using System;

namespace SBox.Inventory;

/// <summary>Limits instance records in this container; one record occupies one slot.</summary>
public sealed class SlotLimitRule : IInventoryRule
{
	public int Maximum { get; }
	public SlotLimitRule( int maximum )
	{
		if ( maximum < 0 ) throw new ArgumentOutOfRangeException( nameof( maximum ) );
		Maximum = maximum;
	}
	public InventoryError Validate( InventoryRuleContext context ) =>
		context.After.Count <= Maximum ? InventoryError.None : InventoryError.SlotLimitExceeded;
}
