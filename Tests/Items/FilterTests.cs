using SBox.Inventory;

internal static class FilterTests
{
	public static int Run()
	{
		var actor = Guid.NewGuid();
		var food = Guid.NewGuid(); var tool = Guid.NewGuid(); var unknown = Guid.NewGuid();
		var service = new ContainerService( (who, _, _) => who == actor );
		foreach ( var type in new[] { food, tool, unknown } ) service.TryRegisterDefinition( type );
		var definition = new ContainerDefinition( Guid.NewGuid(), "Storage" );
		var source = service.Create( definition ); var target = service.Create( definition );
		var allowed = new List<Guid> { food, food };
		var typeRule = new AllowedItemTypesRule( allowed );
		allowed.Add( tool );
		Require( typeRule.AllowedTypes.Count == 1 );
		service.TrySetRules( target.ContainerId, new[] { typeRule } );
		int events = 0; service.Changed += _ => events++;
		var item = service.TryAdd( actor, source.ContainerId, tool, 3 ).Item;
		Expect( service.TryTransfer( actor, source.ContainerId, target.ContainerId, item.InstanceId, 1 ), InventoryError.ItemTypeNotAllowed );
		Expect( service.TryTransfer( actor, source.ContainerId, target.ContainerId, item.InstanceId, 3 ), InventoryError.ItemTypeNotAllowed );
		Expect( service.TryAdd( actor, target.ContainerId, tool, 1 ), InventoryError.ItemTypeNotAllowed );
		Require( events == 1 && source.Inventory.Count( tool ) == 3 && target.Inventory.Items.Count == 0 );
		Require( service.TryAdd( actor, target.ContainerId, food, 2 ).Success );
		Console.WriteLine( "PASS Type allowlist rejects add and full/partial transfers without mutation" );

		var categories = new Dictionary<Guid, string> { [food] = "food", [tool] = "tools" };
		var names = new List<string> { "food", "food" };
		var categoryRule = new AllowedItemCategoriesRule( names, categories );
		categories[tool] = "food"; names.Add( "tools" );
		Require( categoryRule.AllowedCategories.Count == 1 );
		service.TrySetRules( target.ContainerId, new[] { categoryRule } );
		var before = events;
		Expect( service.TryAdd( actor, target.ContainerId, tool, 1 ), InventoryError.ItemCategoryNotAllowed );
		Expect( service.TryAdd( actor, target.ContainerId, unknown, 1 ), InventoryError.UnknownItemCategory );
		Expect( service.TryTransfer( actor, source.ContainerId, target.ContainerId, item.InstanceId, 1 ), InventoryError.ItemCategoryNotAllowed );
		Require( events == before && source.Inventory.Count( tool ) == 3 && target.Inventory.Count( food ) == 2 );
		var old = target.Rules;
		Expect( service.TrySetRules( target.ContainerId, new[] { new AllowedItemCategoriesRule( new[] { "Food" }, categoryRule.Categories ) } ), InventoryError.ItemCategoryNotAllowed );
		Require( target.Rules == old );
		Console.WriteLine( "PASS Category snapshots, ordinal IDs and unknown categories fail closed" );

		var weights = new Dictionary<Guid, decimal> { [food] = 0.5m };
		service.TrySetRules( target.ContainerId, new IInventoryRule[] { typeRule, categoryRule, new WeightLimitRule( 2m, weights ), new SlotLimitRule( 2 ), new QuantityLimitRule( 4 ) } );
		var foodItem = service.TryAdd( actor, source.ContainerId, food, 4 ).Item;
		before = events;
		Expect( service.TryTransfer( actor, source.ContainerId, target.ContainerId, foodItem.InstanceId, 3 ), InventoryError.WeightLimitExceeded );
		Require( events == before && source.Inventory.Count( food ) == 4 );
		Require( service.TryTransfer( actor, source.ContainerId, target.ContainerId, foodItem.InstanceId, 2 ).Success );
		Expect( service.TryAdd( actor, target.ContainerId, food, 1 ), InventoryError.WeightLimitExceeded );
		Require( target.Inventory.Count( food ) == 4 && source.Inventory.Count( food ) == 2 );
		Console.WriteLine( "PASS Filters compose with weight, slots and quantity before commit" );

		var empty = service.Create( definition );
		service.TrySetRules( empty.ContainerId, new[] { new AllowedItemTypesRule( Array.Empty<Guid>() ) } );
		Expect( service.TryAdd( actor, empty.ContainerId, food, 1 ), InventoryError.ItemTypeNotAllowed );
		service.TrySetRules( empty.ContainerId, new[] { new AllowedItemCategoriesRule( Array.Empty<string>(), categoryRule.Categories ) } );
		Expect( service.TryAdd( actor, empty.ContainerId, food, 1 ), InventoryError.ItemCategoryNotAllowed );
		Expect( service.TrySetRules( target.ContainerId, new[] { new AllowedItemTypesRule( Array.Empty<Guid>() ) } ), InventoryError.ItemTypeNotAllowed );
		service.TrySetRules( target.ContainerId, Array.Empty<IInventoryRule>() );
		Require( service.TryAdd( actor, target.ContainerId, tool, 1 ).Success );
		Console.WriteLine( "PASS Empty lists deny contents; removing filters restores unrestricted types" );

		var filteredItem = service.TryAdd( actor, empty.ContainerId, tool, 1 );
		Require( !filteredItem.Success );
		service.TrySetRules( source.ContainerId, new IInventoryRule[] { new AllowedItemTypesRule( new[] { food, tool } ), new AllowedItemCategoriesRule( new[] { "food", "tools" }, categoryRule.Categories ) } );
		Require( service.TryRemove( actor, source.ContainerId, item.InstanceId, 1 ).Success );
		Require( source.Inventory.Count( tool ) == 2 );
		Console.WriteLine( "PASS Removal remains valid under configured filters" );

		Throws<ArgumentNullException>( () => new AllowedItemTypesRule( null ) );
		Throws<ArgumentException>( () => new AllowedItemTypesRule( new[] { Guid.Empty } ) );
		Throws<ArgumentNullException>( () => new AllowedItemCategoriesRule( null, categories ) );
		Throws<ArgumentNullException>( () => new AllowedItemCategoriesRule( names, null ) );
		foreach ( var name in new[] { null, "", " " } )
		{
			Throws<ArgumentException>( () => new AllowedItemCategoriesRule( new[] { name }, categories ) );
			Throws<ArgumentException>( () => new AllowedItemCategoriesRule( names, new Dictionary<Guid, string> { [food] = name } ) );
		}
		Throws<ArgumentException>( () => new AllowedItemCategoriesRule( names, new Dictionary<Guid, string> { [Guid.Empty] = "food" } ) );
		Throws<NotSupportedException>( () => ((IList<Guid>)typeRule.AllowedTypes).Clear() );
		Throws<NotSupportedException>( () => ((IDictionary<Guid, string>)categoryRule.Categories)[tool] = "food" );
		Console.WriteLine( "PASS Invalid filter configuration and external mutation rejected" );
		return 6;
	}
	private static void Require( bool condition )
	{
		if ( !condition ) throw new Exception( "Filter assertion failed." );
	}
	private static void Expect( InventoryResult result, InventoryError error ) => Require( result.Error == error );
	private static void Throws<T>( Action action ) where T : Exception
	{
		try { action(); }
		catch ( T ) { return; }
		throw new Exception( $"Expected {typeof( T ).Name}." );
	}
}
