using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SBox.Inventory;

/// <summary>
/// Limits raw weight of direct contents. Units are chosen by the game (e.g. kg).
/// Reduction affects reported carried weight only, never storage capacity.
/// Nested contents and the empty container's own weight are not included.
/// </summary>
public sealed class WeightLimitRule : IInventoryRule
{
	public decimal Maximum { get; }
	/// <summary>Fraction from zero to one. One means 100 percent carried-weight reduction.</summary>
	public decimal ReductionFraction { get; }
	/// <summary>Immutable snapshot keyed by stable item definition IDs.</summary>
	public IReadOnlyDictionary<Guid, decimal> UnitWeights { get; }

	public WeightLimitRule( decimal maximum, IReadOnlyDictionary<Guid, decimal> unitWeights,
		decimal reductionFraction = 0m )
	{
		if ( maximum < 0m ) throw new ArgumentOutOfRangeException( nameof( maximum ) );
		if ( reductionFraction < 0m || reductionFraction > 1m )
			throw new ArgumentOutOfRangeException( nameof( reductionFraction ) );
		if ( unitWeights is null ) throw new ArgumentNullException( nameof( unitWeights ) );
		var copy = new Dictionary<Guid, decimal>();
		foreach ( var pair in unitWeights )
		{
			if ( pair.Key == Guid.Empty || pair.Value < 0m )
				throw new ArgumentException( "Weights require non-empty definition IDs and non-negative values.", nameof( unitWeights ) );
			copy.Add( pair.Key, pair.Value );
		}
		Maximum = maximum;
		ReductionFraction = reductionFraction;
		UnitWeights = new ReadOnlyDictionary<Guid, decimal>( copy );
	}

	public InventoryError Validate( InventoryRuleContext context )
	{
		if ( context is null ) throw new ArgumentNullException( nameof( context ) );
		var error = TryMeasure( context.After, out var raw, out _ );
		if ( error != InventoryError.None ) return error;
		return raw <= Maximum ? InventoryError.None : InventoryError.WeightLimitExceeded;
	}

	/// <summary>
	/// Measures a snapshot without changing state. Unknown IDs are never treated as
	/// weightless. On failure both outputs are zero; zero is not a successful reading.
	/// Decimal arithmetic is checked; rounding follows .NET decimal semantics.
	/// </summary>
	public InventoryError TryMeasure( IReadOnlyList<ItemInstance> items, out decimal rawWeight, out decimal carriedWeight )
	{
		rawWeight = 0m;
		carriedWeight = 0m;
		if ( items is null ) return InventoryError.InvalidWeightData;
		decimal total = 0m;
		foreach ( var item in items )
		{
			if ( item is null ) return InventoryError.InvalidWeightData;
			if ( !UnitWeights.TryGetValue( item.DefinitionId, out var unitWeight ) )
				return InventoryError.UnknownItemWeight;
			try { total = checked( total + checked( unitWeight * item.Amount ) ); }
			catch ( OverflowException ) { return InventoryError.WeightOverflow; }
		}
		rawWeight = total;
		carriedWeight = total * (1m - ReductionFraction);
		return InventoryError.None;
	}
}
