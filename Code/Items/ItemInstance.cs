using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SBox.Inventory;

/// <summary>
/// Immutable item data with no scene or engine dependency. Inventory operations will
/// replace snapshots rather than allowing callers to mutate owned state.
/// </summary>
public sealed class ItemInstance
{
	/// <summary>Unique identity of this instance. Ownership uniqueness is enforced by inventory operations.</summary>
	public Guid InstanceId { get; }

	/// <summary>Stable identity of the item type, independent of resource paths.</summary>
	public Guid DefinitionId { get; }

	/// <summary>Positive quantity represented by this instance; no stacking policy is implied.</summary>
	public int Amount { get; }

	/// <summary>Defensively copied, ordinal, case-sensitive instance properties.</summary>
	public IReadOnlyDictionary<string, string> Properties { get; }

	/// <summary>Creates a validated snapshot. Invalid caller data raises an argument exception.</summary>
	public ItemInstance( Guid instanceId, Guid definitionId, int amount,
		IReadOnlyDictionary<string, string> properties = null )
	{
		if ( instanceId == Guid.Empty )
			throw new ArgumentException( "Instance ID must not be empty.", nameof( instanceId ) );
		if ( definitionId == Guid.Empty )
			throw new ArgumentException( "Definition ID must not be empty.", nameof( definitionId ) );
		if ( amount <= 0 )
			throw new ArgumentOutOfRangeException( nameof( amount ), "Amount must be positive." );

		var copy = new Dictionary<string, string>( StringComparer.Ordinal );
		if ( properties is not null )
		{
			foreach ( var pair in properties )
			{
				if ( string.IsNullOrWhiteSpace( pair.Key ) || pair.Value is null )
					throw new ArgumentException( "Property keys must not be blank and values must not be null.", nameof( properties ) );
				copy.Add( pair.Key, pair.Value );
			}
		}

		InstanceId = instanceId;
		DefinitionId = definitionId;
		Amount = amount;
		Properties = new ReadOnlyDictionary<string, string>( copy );
	}
}
