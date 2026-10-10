using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SBox.Inventory;

/// <summary>
/// Limits each instance quantity by type. A maximum of one means non-stackable.
/// Unknown types fail closed; no implicit splitting or merging is performed.
/// </summary>
public sealed class StackLimitRule : IInventoryRule
{
	public IReadOnlyDictionary<Guid, int> Maximums { get; }
	public StackLimitRule( IReadOnlyDictionary<Guid, int> maximums )
	{
		if ( maximums is null ) throw new ArgumentNullException( nameof( maximums ) );
		var copy = new Dictionary<Guid, int>();
		foreach ( var pair in maximums )
		{
			if ( pair.Key == Guid.Empty || pair.Value <= 0 )
				throw new ArgumentException( "Stack limits require non-empty type IDs and positive maximums.", nameof( maximums ) );
			copy.Add( pair.Key, pair.Value );
		}
		Maximums = new ReadOnlyDictionary<Guid, int>( copy );
	}
	public InventoryError Validate( InventoryRuleContext context )
	{
		if ( context is null ) throw new ArgumentNullException( nameof( context ) );
		foreach ( var item in context.After )
		{
			if ( !Maximums.TryGetValue( item.DefinitionId, out var maximum ) ) return InventoryError.UnknownStackLimit;
			if ( item.Amount > maximum ) return InventoryError.StackLimitExceeded;
		}
		return InventoryError.None;
	}
}
