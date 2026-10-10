using SBox.Inventory;

internal static class WeightTests
{
	public static int Run()
	{
		var actor = Guid.NewGuid();
		var type = Guid.NewGuid();
		var unknown = Guid.NewGuid();
		var service = new ContainerService( (who, _, _) => who == actor );
		service.TryRegisterDefinition( type ); service.TryRegisterDefinition( unknown );
		var definition = new ContainerDefinition( Guid.NewGuid(), "Storage" );
		var source = service.Create( definition );
		var target = service.Create( definition );
		var weights = new Dictionary<Guid, decimal> { [type] = 0.25m };
		var rule = new WeightLimitRule( 1m, weights, 0.5m );
		weights[type] = 0m;
		service.TrySetRules( target.ContainerId, new IInventoryRule[] { rule, new SlotLimitRule( 2 ), new QuantityLimitRule( 8 ) } );
		int events = 0;
		service.Changed += _ => events++;
		var item = service.TryAdd( actor, source.ContainerId, type, 8 ).Item;
		Expect( service.TryTransfer( actor, source.ContainerId, target.ContainerId, item.InstanceId, 5 ), InventoryError.WeightLimitExceeded );
		Require( events == 1 && source.Inventory.Count( type ) == 8 && target.Inventory.Items.Count == 0 );
		var moved = service.TryTransfer( actor, source.ContainerId, target.ContainerId, item.InstanceId, 4 );
		Require( moved.Success );
		Require( rule.TryMeasure( target.Inventory.Items, out var raw, out var carried ) == InventoryError.None && raw == 1m && carried == 0.5m );
		Expect( service.TryAdd( actor, target.ContainerId, type, 1 ), InventoryError.WeightLimitExceeded );
		Require( events == 2 && target.Inventory.Count( type ) == 4 && source.Inventory.Count( type ) == 4 );
		Console.WriteLine( "PASS Exact decimal capacity, reduction and failed-transfer conservation" );

		service.TryRemove( actor, target.ContainerId, moved.Item.InstanceId, 1 );
		Require( rule.TryMeasure( target.Inventory.Items, out raw, out carried ) == InventoryError.None && raw == 0.75m && carried == 0.375m );
		var old = target.Rules;
		Expect( service.TrySetRules( target.ContainerId, new[] { new WeightLimitRule( 0.5m, rule.UnitWeights ) } ), InventoryError.WeightLimitExceeded );
		Require( target.Rules == old && target.Inventory.Count( type ) == 3 );
		Console.WriteLine( "PASS Removal and incompatible weight-rule replacement" );

		var beforeEvents = events;
		Expect( service.TryAdd( actor, target.ContainerId, unknown, 1 ), InventoryError.UnknownItemWeight );
		var mystery = service.TryAdd( actor, source.ContainerId, unknown, 1 ).Item;
		beforeEvents++;
		Expect( service.TryTransfer( actor, source.ContainerId, target.ContainerId, mystery.InstanceId, 1 ), InventoryError.UnknownItemWeight );
		Require( source.Inventory.Find( mystery.InstanceId ) == mystery && target.Inventory.Count( unknown ) == 0 && events == beforeEvents );
		Console.WriteLine( "PASS Unknown weights fail closed without state or event changes" );

		var zero = service.Create( definition );
		var free = new WeightLimitRule( 0m, new Dictionary<Guid, decimal> { [type] = 0m } );
		service.TrySetRules( zero.ContainerId, new[] { free } );
		Require( service.TryAdd( actor, zero.ContainerId, type, int.MaxValue ).Success );
		Require( free.TryMeasure( zero.Inventory.Items, out raw, out carried ) == InventoryError.None && raw == 0m && carried == 0m );
		var fullReduction = new WeightLimitRule( 1m, rule.UnitWeights, 1m );
		service.TrySetRules( target.ContainerId, new[] { fullReduction } );
		Require( fullReduction.TryMeasure( target.Inventory.Items, out raw, out carried ) == InventoryError.None && raw == 0.75m && carried == 0m );
		Expect( service.TryAdd( actor, target.ContainerId, type, 2 ), InventoryError.WeightLimitExceeded );
		Console.WriteLine( "PASS Zero unit weight and full reduction never bypass raw capacity" );

		var huge = service.Create( definition );
		var hugeRule = new WeightLimitRule( decimal.MaxValue, new Dictionary<Guid, decimal> { [type] = decimal.MaxValue } );
		service.TrySetRules( huge.ContainerId, new[] { hugeRule } );
		Expect( service.TryAdd( actor, huge.ContainerId, type, 2 ), InventoryError.WeightOverflow );
		Require( huge.Inventory.Items.Count == 0 );
		Require( service.TryAdd( actor, huge.ContainerId, type, 1 ).Success );
		Expect( service.TryAdd( actor, huge.ContainerId, type, 1 ), InventoryError.WeightOverflow );
		Require( huge.Inventory.Count( type ) == 1 );
		Require( hugeRule.TryMeasure( new[] { huge.Inventory.Items[0], huge.Inventory.Items[0] }, out raw, out carried ) == InventoryError.WeightOverflow && raw == 0m && carried == 0m );
		Console.WriteLine( "PASS Multiplication and accumulation overflow fail atomically" );

		Throws<ArgumentOutOfRangeException>( () => new WeightLimitRule( -1m, rule.UnitWeights ) );
		foreach ( var fraction in new[] { -0.01m, 1.01m } )
			Throws<ArgumentOutOfRangeException>( () => new WeightLimitRule( 1m, rule.UnitWeights, fraction ) );
		Throws<ArgumentNullException>( () => new WeightLimitRule( 1m, null ) );
		Throws<ArgumentException>( () => new WeightLimitRule( 1m, new Dictionary<Guid, decimal> { [Guid.Empty] = 1m } ) );
		Throws<ArgumentException>( () => new WeightLimitRule( 1m, new Dictionary<Guid, decimal> { [type] = -1m } ) );
		Throws<NotSupportedException>( () => ((IDictionary<Guid, decimal>)rule.UnitWeights)[type] = 0m );
		Require( rule.TryMeasure( null, out raw, out carried ) == InventoryError.InvalidWeightData );
		Require( rule.TryMeasure( new ItemInstance[] { null }, out raw, out carried ) == InventoryError.InvalidWeightData );
		Console.WriteLine( "PASS Invalid weight inputs and mutable catalogs rejected" );
		return 6;
	}
	private static void Require( bool condition )
	{
		if ( !condition ) throw new Exception( "Weight assertion failed." );
	}
	private static void Expect( InventoryResult result, InventoryError error ) => Require( result.Error == error );
	private static void Throws<T>( Action action ) where T : Exception
	{
		try { action(); }
		catch ( T ) { return; }
		throw new Exception( $"Expected {typeof( T ).Name}." );
	}
}
