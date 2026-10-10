using SBox.Inventory;

internal static class TransferTests
{
	public static int Run()
	{
		var actor = Guid.NewGuid();
		var type = Guid.NewGuid();
		bool denied = false, throwPolicy = false;
		ContainerService service = null;
		service = new ContainerService( (a, c, action) =>
		{
			Expect( service.TryDelete( c.ContainerId ), InventoryError.OperationInProgress );
			if ( throwPolicy ) throw new Exception( "Policy failure" );
			return a == actor && !(denied && action == ContainerAction.TransferIn);
		} );
		Expect( service.TryRegisterDefinition( type ), InventoryError.None );
		var definition = new ContainerDefinition( Guid.NewGuid(), "Storage" );
		var source = service.Create( definition );
		var target = service.Create( definition );
		var sourceId = source.ContainerId;
		var targetId = target.ContainerId;
		var original = service.TryAdd( actor, sourceId, type, 10, new Dictionary<string, string> { ["quality"] = "good" } ).Item;
		var events = new List<ContainerChange>();
		service.Changed += events.Add;
		var partial = service.TryTransfer( actor, sourceId, targetId, original.InstanceId, 4 );
		Require( partial.Success && partial.Item.InstanceId != original.InstanceId && partial.Item.Amount == 4 );
		Require( source.Inventory.Find( original.InstanceId ).Amount == 6 && target.Inventory.Count( type ) == 4 );
		Require( partial.Item.Properties["quality"] == "good" && original.Amount == 10 );
		Require( events.Count == 1 && events[0].TargetId == targetId && events[0].Change.After.Amount == 6 );
		Console.WriteLine( "PASS Partial transfer preserves totals, properties and snapshots" );

		var full = service.TryTransfer( actor, sourceId, targetId, original.InstanceId, 6 );
		Require( full.Success && full.Item.InstanceId == original.InstanceId );
		Require( source.Inventory.Items.Count == 0 && target.Inventory.Count( type ) == 10 );
		Expect( service.TryTransfer( actor, sourceId, targetId, original.InstanceId, 6 ), InventoryError.ItemNotFound );
		Require( events.Count == 2 );
		Console.WriteLine( "PASS Full transfer retains identity and cannot be replayed" );

		foreach ( var amount in new[] { 0, -1, int.MinValue } )
			Expect( service.TryTransfer( actor, targetId, sourceId, original.InstanceId, amount ), InventoryError.InvalidAmount );
		Expect( service.TryTransfer( actor, targetId, sourceId, original.InstanceId, 7 ), InventoryError.InsufficientAmount );
		Expect( service.TryTransfer( actor, targetId, targetId, original.InstanceId, 1 ), InventoryError.SameContainer );
		Expect( service.TryTransfer( actor, Guid.Empty, sourceId, original.InstanceId, 1 ), InventoryError.InvalidId );
		Expect( service.TryTransfer( actor, targetId, Guid.NewGuid(), original.InstanceId, 1 ), InventoryError.ContainerNotFound );
		Require( events.Count == 2 && target.Inventory.Count( type ) == 10 );
		Console.WriteLine( "PASS Invalid transfers leave both states and events unchanged" );

		denied = true;
		Expect( service.TryTransfer( actor, targetId, sourceId, original.InstanceId, 1 ), InventoryError.AccessDenied );
		denied = false; throwPolicy = true;
		Expect( service.TryTransfer( actor, targetId, sourceId, original.InstanceId, 1 ), InventoryError.AccessPolicyFailed );
		throwPolicy = false;
		Expect( service.TryAdd( Guid.NewGuid(), sourceId, type, 1 ), InventoryError.AccessDenied );
		Expect( service.TryRemove( Guid.NewGuid(), targetId, original.InstanceId, 1 ), InventoryError.AccessDenied );
		service.TrySetAvailable( sourceId, false );
		Expect( service.TryTransfer( actor, targetId, sourceId, original.InstanceId, 1 ), InventoryError.ContainerUnavailable );
		Expect( service.TryAdd( actor, sourceId, type, 1 ), InventoryError.ContainerUnavailable );
		service.TrySetAvailable( sourceId, true );
		Require( target.Inventory.Count( type ) == 10 && events.Count == 2 );
		Console.WriteLine( "PASS Access, policy exceptions and availability fail atomically" );

		Expect( service.TryDelete( targetId ), InventoryError.ContainerNotEmpty );
		Expect( service.TryDelete( sourceId ), InventoryError.None );
		Require( service.Find( sourceId ) is null && !source.IsAvailable );
		Expect( service.TryTransfer( actor, targetId, sourceId, original.InstanceId, 1 ), InventoryError.ContainerNotFound );
		Expect( service.TryDelete( sourceId ), InventoryError.ContainerNotFound );
		Console.WriteLine( "PASS Nonempty deletion rejected and deleted containers unavailable" );

		var another = service.Create( definition );
		var maximum = service.TryAdd( actor, targetId, type, int.MaxValue ).Item;
		service.TryAdd( actor, another.ContainerId, type, int.MaxValue );
		Require( service.TryTransfer( actor, targetId, another.ContainerId, maximum.InstanceId, int.MaxValue ).Success );
		Require( another.Inventory.Count( type ) == 2L * int.MaxValue );
		Console.WriteLine( "PASS Maximum quantity transfer and long destination totals" );

		int notified = 0;
		service.Changed += change =>
		{
			Require( target.Inventory.Find( maximum.InstanceId ) is not null && another.Inventory.Find( maximum.InstanceId ) is null );
			Expect( service.TryRemove( actor, targetId, maximum.InstanceId, 1 ), InventoryError.OperationInProgress );
			Expect( service.TrySetAvailable( targetId, false ), InventoryError.OperationInProgress );
			throw new InvalidOperationException( "Observer failure" );
		};
		service.Changed += _ => notified++;
		var moved = service.TryTransfer( actor, another.ContainerId, targetId, maximum.InstanceId, int.MaxValue );
		Require( moved.Success && moved.NotificationErrors.Count == 1 && notified == 1 );
		Require( moved.NotificationErrors[0].Message == "Observer failure" );
		Console.WriteLine( "PASS Transfer observers see both commits; failures and reentrancy isolated" );
		return 7;
	}
	private static void Require( bool condition )
	{
		if ( !condition ) throw new Exception( "Transfer assertion failed." );
	}
	private static void Expect( InventoryResult result, InventoryError error ) => Require( result.Error == error );
}
