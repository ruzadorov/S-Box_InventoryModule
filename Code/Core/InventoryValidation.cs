using System;
using System.Collections.Generic;

namespace SBox.Inventory;

/// <summary>Pure validation contract. Rules must not mutate state or retain mutable game data.</summary>
public interface IInventoryRule
{
	InventoryError Validate( InventoryRuleContext context );
}

/// <summary>Immutable membership snapshots before and after a proposed operation.</summary>
public sealed class InventoryRuleContext
{
	public Guid InventoryId { get; }
	public IReadOnlyList<ItemInstance> Before { get; }
	public IReadOnlyList<ItemInstance> After { get; }
	internal InventoryRuleContext( Inventory inventory, InventoryChange change ) : this( inventory, change, null ) { }
	internal InventoryRuleContext( Inventory inventory, InventoryChange change, InventoryChange second )
	{
		InventoryId = inventory.InventoryId;
		Before = inventory.Items;
		var after = new List<ItemInstance>();
		foreach ( var item in Before )
		{
			var replacement = change?.Before?.InstanceId == item.InstanceId ? change :
				second?.Before?.InstanceId == item.InstanceId ? second : null;
			if ( replacement is not null )
			{
				if ( replacement.After is not null ) after.Add( replacement.After );
			}
			else after.Add( item );
		}
		if ( change?.Before is null && change?.After is not null ) after.Add( change.After );
		if ( second?.Before is null && second?.After is not null ) after.Add( second.After );
		After = after.AsReadOnly();
	}
}

/// <summary>Ordered composition. First rejection wins; exceptions fail closed.</summary>
public sealed class InventoryRuleSet
{
	public static InventoryRuleSet Empty { get; } = new( Array.Empty<IInventoryRule>() );
	public IReadOnlyList<IInventoryRule> Rules { get; }
	public InventoryRuleSet( IReadOnlyList<IInventoryRule> rules )
	{
		if ( rules is null ) throw new ArgumentNullException( nameof( rules ) );
		var copy = new List<IInventoryRule>();
		foreach ( var rule in rules )
			copy.Add( rule ?? throw new ArgumentException( "Rules must not contain null.", nameof( rules ) ) );
		Rules = copy.AsReadOnly();
	}
	public InventoryError Validate( InventoryRuleContext context )
	{
		if ( context is null ) throw new ArgumentNullException( nameof( context ) );
		foreach ( var rule in Rules )
		{
			InventoryError error;
			try { error = rule.Validate( context ); }
			catch ( Exception ) { return InventoryError.RuleFailed; }
			if ( error != InventoryError.None ) return error;
		}
		return InventoryError.None;
	}
}
