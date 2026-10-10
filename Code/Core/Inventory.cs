using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SBox.Inventory;

/// <summary>Read-only inventory view. Owned and mutated exclusively by its service.</summary>
public sealed class Inventory
{
	private readonly Dictionary<Guid, ItemInstance> _items = new();
	public Guid InventoryId { get; } = Guid.NewGuid();
	/// <summary>Stable membership snapshot containing immutable items.</summary>
	public IReadOnlyList<ItemInstance> Items => new ReadOnlyCollection<ItemInstance>( new List<ItemInstance>( _items.Values ) );
	internal Inventory() { }
	/// <summary>Returns the current instance snapshot, or null for an absent/empty ID.</summary>
	public ItemInstance Find( Guid instanceId ) => _items.TryGetValue( instanceId, out var item ) ? item : null;
	/// <summary>Total quantity of a type. Uses long so multiple int-sized instances cannot overflow int.</summary>
	public long Count( Guid definitionId )
	{
		long count = 0;
		foreach ( var item in _items.Values )
			if ( item.DefinitionId == definitionId ) count += item.Amount;
		return count;
	}
	internal void Add( ItemInstance item ) => _items.Add( item.InstanceId, item );
	internal void Replace( ItemInstance item ) => _items[item.InstanceId] = item;
	internal void Remove( Guid id ) => _items.Remove( id );
}

/// <summary>Committed before/after snapshots; null represents absence.</summary>
public sealed class InventoryChange
{
	public ItemInstance Before { get; }
	public ItemInstance After { get; }
	internal InventoryChange( ItemInstance before, ItemInstance after )
	{
		Before = before;
		After = after;
	}
}
