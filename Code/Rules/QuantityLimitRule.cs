using System;

namespace SBox.Inventory;

/// <summary>Limits total unit quantity of all item types in this container.</summary>
public sealed class QuantityLimitRule : IInventoryRule
{
	public long Maximum { get; }
	public QuantityLimitRule( long maximum )
	{
		if ( maximum < 0 ) throw new ArgumentOutOfRangeException( nameof( maximum ) );
		Maximum = maximum;
	}
	public InventoryError Validate( InventoryRuleContext context )
	{
		long remaining = Maximum;
		foreach ( var item in context.After )
		{
			if ( item.Amount > remaining ) return InventoryError.QuantityLimitExceeded;
			remaining -= item.Amount;
		}
		return InventoryError.None;
	}
}
