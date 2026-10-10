using SBox.Inventory;

internal static class StackTests
{
	public static int Run()
	{
		var f = new Fixture();
		var input = new Dictionary<Guid, int> { [f.Type] = 6 };
		var stackRule = new StackLimitRule( input ); input[f.Type] = 100;
		f.Service.TrySetRules( f.Target.ContainerId, new IInventoryRule[] { stackRule, new SlotLimitRule( 1 ) } );
		f.Service.TrySetRules( f.Source.ContainerId, new[] { stackRule } );
		Expect( f.Add( f.Target, 7 ), InventoryError.StackLimitExceeded );
		var source = f.Add( f.Source, 3 ).Item; var target = f.Add( f.Target, 2 ).Item;
		var partial = f.Merge( source, target, 2 );
		Require( partial.Success && partial.Item.InstanceId == target.InstanceId && partial.Item.Amount == 4 );
		Require( f.Source.Inventory.Find( source.InstanceId ).Amount == 1 && source.Amount == 3 && target.Amount == 2 );
		Require( f.Merge( source, target, 1 ).Success && f.Source.Inventory.Find( source.InstanceId ) is null );
		Require( f.Target.Inventory.Find( target.InstanceId ).Amount == 5 && f.Target.Inventory.Items.Count == 1 );
		Expect( f.Merge( source, target, 1 ), InventoryError.ItemNotFound );
		Console.WriteLine( "PASS Stack limit, partial/full merge and destination identity conservation" );

		f = new Fixture(); source = f.Add( f.Source, 4 ).Item; target = f.Add( f.Target, 5 ).Item;
		int events = 0; f.Service.Changed += _ => events++;
		foreach ( var rule in new IInventoryRule[] { new StackLimitRule( new Dictionary<Guid, int> { [f.Type] = 6 } ), new QuantityLimitRule( 6 ), new WeightLimitRule( 6m, new Dictionary<Guid, decimal> { [f.Type] = 1m } ) } )
		{
			f.Service.TrySetRules( f.Target.ContainerId, new IInventoryRule[] { rule, new SlotLimitRule( 1 ), new AllowedItemTypesRule( new[] { f.Type } ) } );
			var result = f.Merge( source, target, 2 );
			Require( !result.Success && result.Error is InventoryError.StackLimitExceeded or InventoryError.QuantityLimitExceeded or InventoryError.WeightLimitExceeded );
			Require( f.Source.Inventory.Count( f.Type ) == 4 && f.Target.Inventory.Count( f.Type ) == 5 && events == 0 );
		}
		f.Service.TrySetRules( f.Target.ContainerId, new IInventoryRule[]
		{
			new StackLimitRule( new Dictionary<Guid, int> { [f.Type] = 10 } ), new QuantityLimitRule( 10 ),
			new WeightLimitRule( 10m, new Dictionary<Guid, decimal> { [f.Type] = 1m } ), new SlotLimitRule( 1 ),
			new AllowedItemTypesRule( new[] { f.Type } ), new AllowedItemCategoriesRule( new[] { "supplies" }, new Dictionary<Guid, string> { [f.Type] = "supplies" } )
		} );
		Require( f.Merge( source, target, 2 ).Success && events == 1 );
		Console.WriteLine( "PASS Stack/quantity/weight rejection and composition with slots and filters" );

		f = new Fixture(); source = f.Add( f.Source, 2 ).Item; target = f.Add( f.Source, 3 ).Item;
		f.Service.TrySetRules( f.Source.ContainerId, new IInventoryRule[] { new QuantityLimitRule( 5 ), new SlotLimitRule( 2 ), new StackLimitRule( new Dictionary<Guid, int> { [f.Type] = 5 } ), new WeightLimitRule( 5m, new Dictionary<Guid, decimal> { [f.Type] = 1m } ) } );
		var same = f.Service.TryMergeStacks( f.Actor, f.Source.ContainerId, f.Source.ContainerId, source.InstanceId, target.InstanceId, 2 );
		Require( same.Success && same.Item.Amount == 5 && f.Source.Inventory.Items.Count == 1 && f.Source.Inventory.Count( f.Type ) == 5 );
		Console.WriteLine( "PASS Same-container merge validates final projection without double counting" );

		f = new Fixture();
		var props = new Dictionary<string, string> { ["quality"] = "good", ["tag"] = "x" };
		source = f.Add( f.Source, 2, props ).Item;
		target = f.Add( f.Target, 2, new Dictionary<string, string> { ["tag"] = "x", ["quality"] = "good" } ).Item;
		Require( f.Merge( source, target, 1 ).Success );
		foreach ( var different in new[] { new Dictionary<string, string> { ["quality"] = "bad", ["tag"] = "x" }, new Dictionary<string, string> { ["Quality"] = "good", ["tag"] = "x" }, new Dictionary<string, string> { ["quality"] = "good" } } )
		{
			var incompatible = f.Add( f.Source, 1, different ).Item;
			Expect( f.Merge( incompatible, target, 1 ), InventoryError.IncompatibleStacks );
			Require( f.Source.Inventory.Find( incompatible.InstanceId ) == incompatible );
		}
		var otherType = Guid.NewGuid(); f.Service.TryRegisterDefinition( otherType );
		var otherItem = f.Service.TryAdd( f.Actor, f.Source.ContainerId, otherType, 1 ).Item;
		Expect( f.Merge( otherItem, target, 1 ), InventoryError.IncompatibleStacks );
		Console.WriteLine( "PASS Compatibility requires equal type and complete ordinal properties" );

		f = new Fixture(); source = f.Add( f.Source, 1 ).Item; target = f.Add( f.Target, int.MaxValue ).Item;
		Expect( f.Merge( source, target, 1 ), InventoryError.QuantityOverflow );
		Require( f.Source.Inventory.Count( f.Type ) == 1 && f.Target.Inventory.Count( f.Type ) == int.MaxValue );
		foreach ( var amount in new[] { 0, -1, int.MinValue } ) Expect( f.Merge( source, target, amount ), InventoryError.InvalidAmount );
		Expect( f.Merge( source, target, 2 ), InventoryError.InsufficientAmount );
		Expect( f.Service.TryMergeStacks( f.Actor, f.Source.ContainerId, f.Target.ContainerId, Guid.Empty, target.InstanceId, 1 ), InventoryError.InvalidId );
		Expect( f.Service.TryMergeStacks( f.Actor, f.Source.ContainerId, f.Source.ContainerId, source.InstanceId, source.InstanceId, 1 ), InventoryError.IncompatibleStacks );
		Console.WriteLine( "PASS Overflow and invalid merge inputs leave both stacks unchanged" );

		f = new Fixture(); source = f.Add( f.Source, 2 ).Item; target = f.Add( f.Target, 2 ).Item;
		Expect( f.Service.TrySetRules( f.Target.ContainerId, new[] { new StackLimitRule( new Dictionary<Guid, int> { [f.Type] = 1 } ) } ), InventoryError.StackLimitExceeded );
		var empty = f.Service.Create( f.Definition );
		f.Service.TrySetRules( empty.ContainerId, new[] { new StackLimitRule( new Dictionary<Guid, int>() ) } );
		Expect( f.Add( empty, 1 ), InventoryError.UnknownStackLimit );
		Expect( f.Service.TryTransfer( f.Actor, f.Source.ContainerId, empty.ContainerId, source.InstanceId, 1 ), InventoryError.UnknownStackLimit );
		f.Service.TrySetAvailable( f.Target.ContainerId, false );
		Expect( f.Merge( source, target, 1 ), InventoryError.ContainerUnavailable );
		f.Service.TrySetAvailable( f.Target.ContainerId, true );
		Expect( f.Service.TryMergeStacks( Guid.NewGuid(), f.Source.ContainerId, f.Target.ContainerId, source.InstanceId, target.InstanceId, 1 ), InventoryError.AccessDenied );
		Console.WriteLine( "PASS Configuration, unknown limits and access remain controlled" );

		int notified = 0;
		f.Service.Changed += change =>
		{
			Require( f.Source.Inventory.Find( source.InstanceId ) is null && f.Target.Inventory.Find( target.InstanceId ).Amount == 4 );
			Expect( f.Merge( source, target, 1 ), InventoryError.OperationInProgress );
			throw new InvalidOperationException( "Stack observer failure" );
		};
		f.Service.Changed += _ => notified++;
		var committed = f.Merge( source, target, 2 );
		Require( committed.Success && notified == 1 && committed.NotificationErrors.Count == 1 && committed.NotificationErrors[0].Message == "Stack observer failure" );
		Console.WriteLine( "PASS Merge notification after both commits isolates failures and reentrancy" );

		Throws<ArgumentNullException>( () => new StackLimitRule( null ) );
		Throws<ArgumentException>( () => new StackLimitRule( new Dictionary<Guid, int> { [Guid.Empty] = 2 } ) );
		foreach ( var maximum in new[] { 0, -1 } ) Throws<ArgumentException>( () => new StackLimitRule( new Dictionary<Guid, int> { [f.Type] = maximum } ) );
		Throws<NotSupportedException>( () => ((IDictionary<Guid, int>)stackRule.Maximums).Clear() );
		Console.WriteLine( "PASS Stack configuration is validated and immutable" );
		return 8;
	}
	private sealed class Fixture
	{
		public Guid Actor { get; } = Guid.NewGuid();
		public Guid Type { get; } = Guid.NewGuid();
		public ContainerDefinition Definition { get; } = new( Guid.NewGuid(), "Storage" );
		public ContainerService Service { get; }
		public InventoryContainer Source { get; }
		public InventoryContainer Target { get; }
		public Fixture()
		{
			Service = new ContainerService( (who, _, _) => who == Actor );
			Service.TryRegisterDefinition( Type ); Source = Service.Create( Definition ); Target = Service.Create( Definition );
		}
		public InventoryResult Add( InventoryContainer container, int amount, IReadOnlyDictionary<string, string> properties = null ) => Service.TryAdd( Actor, container.ContainerId, Type, amount, properties );
		public InventoryResult Merge( ItemInstance source, ItemInstance target, int amount ) => Service.TryMergeStacks( Actor, Source.ContainerId, Target.ContainerId, source.InstanceId, target.InstanceId, amount );
	}
	private static void Require( bool condition )
	{
		if ( !condition ) throw new Exception( "Stack assertion failed." );
	}
	private static void Expect( InventoryResult result, InventoryError error ) => Require( result.Error == error );
	private static void Throws<T>( Action action ) where T : Exception
	{
		try { action(); }
		catch ( T ) { return; }
		throw new Exception( $"Expected {typeof( T ).Name}." );
	}
}
