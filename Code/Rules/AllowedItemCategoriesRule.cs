using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SBox.Inventory;

/// <summary>
/// Allows explicitly listed category IDs. Each type has one authored category;
/// ordinal case-sensitive identity, independent of localized display names.
/// Unknown categories fail closed. Applies only to direct contents.
/// </summary>
public sealed class AllowedItemCategoriesRule : IInventoryRule
{
	private readonly HashSet<string> _allowed = new( StringComparer.Ordinal );
	public IReadOnlyList<string> AllowedCategories { get; }
	public IReadOnlyDictionary<Guid, string> Categories { get; }
	public AllowedItemCategoriesRule( IEnumerable<string> allowedCategories, IReadOnlyDictionary<Guid, string> categories )
	{
		if ( allowedCategories is null ) throw new ArgumentNullException( nameof( allowedCategories ) );
		if ( categories is null ) throw new ArgumentNullException( nameof( categories ) );
		foreach ( var category in allowedCategories )
		{
			if ( string.IsNullOrWhiteSpace( category ) ) throw new ArgumentException( "Category IDs must not be blank.", nameof( allowedCategories ) );
			_allowed.Add( category );
		}
		var copy = new Dictionary<Guid, string>();
		foreach ( var pair in categories )
		{
			if ( pair.Key == Guid.Empty || string.IsNullOrWhiteSpace( pair.Value ) )
				throw new ArgumentException( "Categories require non-empty type IDs and nonblank category IDs.", nameof( categories ) );
			copy.Add( pair.Key, pair.Value );
		}
		AllowedCategories = new List<string>( _allowed ).AsReadOnly();
		Categories = new ReadOnlyDictionary<Guid, string>( copy );
	}
	public InventoryError Validate( InventoryRuleContext context )
	{
		if ( context is null ) throw new ArgumentNullException( nameof( context ) );
		foreach ( var item in context.After )
		{
			if ( !Categories.TryGetValue( item.DefinitionId, out var category ) ) return InventoryError.UnknownItemCategory;
			if ( !_allowed.Contains( category ) ) return InventoryError.ItemCategoryNotAllowed;
		}
		return InventoryError.None;
	}
}
