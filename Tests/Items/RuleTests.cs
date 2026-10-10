using SBox.Inventory;

internal static class RuleTests
{
	public static int Run()
	{
		var actor = Guid.NewGuid();
		var service = new ContainerService( (who, _, _) => who == actor );
		var type = Guid.NewGuid();
		service.TryRegisterDefinition( type );
		var definition = new ContainerDefinition( Guid.NewGuid(), "Storage" );
		var source = service.Create( definition );
		var target = service.Create( definition );
		int events = 0;
		service.Changed += _ => events++;
		var rules = new IInventoryRule[] { new SlotLimitRule( 1 ), new QuantityLimitRule( 5 ) };
		Expect( service.TrySetRules( target.ContainerId, rules ), InventoryError.None );
		rules[0] = new SlotLimitRule( 100 );
		var original = service.TryAdd( actor, source.ContainerId, type, 10 ).Item;
		Expect( service.TryTransfer( actor, source.ContainerId, target.ContainerId, original.InstanceId, 6 ), InventoryError.QuantityLimitExceeded );
		Require( source.Inventory.Count( type ) == 10 && target.Inventory.Count( type ) == 0 && events == 1 );
		var moved = service.TryTransfer( actor, source.ContainerId, target.ContainerId, original.InstanceId, 5 );
		Require( moved.Success && source.Inventory.Count( type ) == 5 && target.Inventory.Count( type ) == 5 );
		Expect( service.TryTransfer( actor, source.ContainerId, target.ContainerId, original.InstanceId, 1 ), InventoryError.SlotLimitExceeded );
		Expect( service.TryAdd( actor, target.ContainerId, type, 1 ), InventoryError.SlotLimitExceeded );
		Require( events == 2 && source.Inventory.Count( type ) == 5 && target.Inventory.Count( type ) == 5 );
		Console.WriteLine( "PASS Composed per-container rules validate transfers before commit" );

		var previous = target.Rules;
		Expect( service.TrySetRules( target.ContainerId, new IInventoryRule[] { new QuantityLimitRule( 4 ) } ), InventoryError.QuantityLimitExceeded );
		Expect( service.TrySetRules( target.ContainerId, null ), InventoryError.InvalidRules );
		Expect( service.TrySetRules( target.ContainerId, new IInventoryRule[] { null } ), InventoryError.InvalidRules );
		Require( target.Rules == previous && target.Inventory.Count( type ) == 5 && events == 2 );
		Console.WriteLine( "PASS Invalid rule configuration preserves existing rules and data" );

		Expect( service.TryRemove( actor, target.ContainerId, moved.Item.InstanceId, 2 ), InventoryError.None );
		Require( target.Inventory.Count( type ) == 3 );
		Expect( service.TrySetRules( target.ContainerId, Array.Empty<IInventoryRule>() ), InventoryError.None );
		Expect( service.TryTransfer( actor, source.ContainerId, target.ContainerId, original.InstanceId, 5 ), InventoryError.None );
		Require( target.Inventory.Count( type ) == 8 && source.Inventory.Count( type ) == 0 );
		Console.WriteLine( "PASS Removal reduces totals and empty rules restore unrestricted storage" );

		var rejectRemoval = new CallbackRule( context => context.After.Count < context.Before.Count ? InventoryError.RuleFailed : InventoryError.None );
		service.TrySetRules( target.ContainerId, new[] { rejectRemoval } );
		var beforeEvents = events;
		Expect( service.TryTransfer( actor, target.ContainerId, source.ContainerId, original.InstanceId, 5 ), InventoryError.RuleFailed );
		Expect( service.TryRemove( actor, target.ContainerId, original.InstanceId, 5 ), InventoryError.RuleFailed );
		Require( target.Inventory.Count( type ) == 8 && source.Inventory.Count( type ) == 0 && events == beforeEvents );
		Console.WriteLine( "PASS Source and removal rules cannot be bypassed by transfer" );

		InventoryRuleContext captured = null;
		var exceptional = new CallbackRule( context =>
		{
			if ( context.After.Count == context.Before.Count ) return InventoryError.None;
			captured = context;
			Expect( service.TryDelete( source.ContainerId ), InventoryError.OperationInProgress );
			throw new InvalidOperationException( "Rule failure" );
		} );
		service.TrySetRules( source.ContainerId, new[] { exceptional } );
		Expect( service.TryAdd( actor, source.ContainerId, type, 1 ), InventoryError.RuleFailed );
		Require( captured.Before.Count == 0 && captured.After.Count == 1 && source.Inventory.Items.Count == 0 );
		Require( events == beforeEvents );
		service.TrySetRules( source.ContainerId, Array.Empty<IInventoryRule>() );
		Require( service.TryAdd( actor, source.ContainerId, type, 2 ).Success );
		Require( captured.After[0].Amount == 1 );
		Console.WriteLine( "PASS Exceptions fail closed, reentrancy blocked, validation snapshots stable" );

		var zero = service.Create( definition );
		service.TrySetRules( zero.ContainerId, new IInventoryRule[] { new SlotLimitRule( 0 ) } );
		Expect( service.TryAdd( actor, zero.ContainerId, type, 1 ), InventoryError.SlotLimitExceeded );
		service.TrySetRules( zero.ContainerId, new IInventoryRule[] { new QuantityLimitRule( 0 ) } );
		Expect( service.TryAdd( actor, zero.ContainerId, type, 1 ), InventoryError.QuantityLimitExceeded );
		service.TrySetRules( zero.ContainerId, new IInventoryRule[] { new QuantityLimitRule( 2L * int.MaxValue ) } );
		Require( service.TryAdd( actor, zero.ContainerId, type, int.MaxValue ).Success );
		Require( service.TryAdd( actor, zero.ContainerId, type, int.MaxValue ).Success );
		Expect( service.TryAdd( actor, zero.ContainerId, type, 1 ), InventoryError.QuantityLimitExceeded );
		Require( zero.Inventory.Count( type ) == 2L * int.MaxValue );
		Console.WriteLine( "PASS Zero and large quantity limits without integer overflow" );

		Throws<ArgumentOutOfRangeException>( () => new SlotLimitRule( -1 ) );
		Throws<ArgumentOutOfRangeException>( () => new QuantityLimitRule( -1 ) );
		Throws<ArgumentNullException>( () => new InventoryRuleSet( null ) );
		Throws<NotSupportedException>( () => ((IList<IInventoryRule>)previous.Rules).Clear() );
		Console.WriteLine( "PASS Invalid limits and external mutation rejected" );
		return 7;
	}
	private sealed class CallbackRule( Func<InventoryRuleContext, InventoryError> callback ) : IInventoryRule
	{
		public InventoryError Validate( InventoryRuleContext context ) => callback( context );
	}
	private static void Require( bool condition )
	{
		if ( !condition ) throw new Exception( "Rule assertion failed." );
	}
	private static void Expect( InventoryResult result, InventoryError error ) => Require( result.Error == error );
	private static void Throws<T>( Action action ) where T : Exception
	{
		try { action(); }
		catch ( T ) { return; }
		throw new Exception( $"Expected {typeof( T ).Name}." );
	}
}
