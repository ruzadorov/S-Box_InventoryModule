using System;
using System.Collections.Generic;

namespace SBox.Inventory;

/// <summary>Allows only explicitly listed stable item type IDs in direct contents.</summary>
public sealed class AllowedItemTypesRule : IInventoryRule
{
	private readonly HashSet<Guid> _allowed = new();
	public IReadOnlyList<Guid> AllowedTypes { get; }
	public AllowedItemTypesRule( IEnumerable<Guid> allowedTypes )
	{
		if ( allowedTypes is null ) throw new ArgumentNullException( nameof( allowedTypes ) );
		foreach ( var id in allowedTypes )
		{
			if ( id == Guid.Empty ) throw new ArgumentException( "Allowed type IDs must not be empty.", nameof( allowedTypes ) );
			_allowed.Add( id );
		}
		AllowedTypes = new List<Guid>( _allowed ).AsReadOnly();
	}
	public InventoryError Validate( InventoryRuleContext context )
	{
		if ( context is null ) throw new ArgumentNullException( nameof( context ) );
		foreach ( var item in context.After )
			if ( !_allowed.Contains( item.DefinitionId ) ) return InventoryError.ItemTypeNotAllowed;
		return InventoryError.None;
	}
}
