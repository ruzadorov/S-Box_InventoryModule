using SBox.Inventory;

internal static class CoreTests
{
	public static int Run()
	{
		var definition = Guid.NewGuid();
		var service = new InventoryService();
		var inventory = service.Inventory;
		Expect( service.TryRegisterDefinition( Guid.Empty ), InventoryError.InvalidId );
		Expect( service.TryRegisterDefinition( definition ), InventoryError.None );
		Expect( service.TryRegisterDefinition( definition ), InventoryError.DuplicateDefinition );
		Expect( service.TryAdd( Guid.Empty, 1 ), InventoryError.InvalidId );
		Expect( service.TryAdd( Guid.NewGuid(), 1 ), InventoryError.UnknownDefinition );
		foreach ( var amount in new[] { 0, -1, int.MinValue } )
			Expect( service.TryAdd( definition, amount ), InventoryError.InvalidAmount );
		Expect( service.TryAdd( definition, 1, new Dictionary<string, string> { [""] = "bad" } ), InventoryError.InvalidProperties );
		Require( inventory.Items.Count == 0 && inventory.Count( definition ) == 0 );
		Console.WriteLine( "PASS Invalid registration/add never mutates state" );

		var changes = new List<InventoryChange>();
		service.Changed += changes.Add;
		var first = service.TryAdd( definition, int.MaxValue );
		var second = service.TryAdd( definition, int.MaxValue );
		Require( first.Success && second.Success && first.Item.InstanceId != second.Item.InstanceId );
		Require( inventory.Count( definition ) == 2L * int.MaxValue );
		Require( changes.Count == 2 && changes[0].Before is null && changes[0].After == first.Item );
		Console.WriteLine( "PASS Unique instances and count beyond int.MaxValue" );

		var snapshot = inventory.Items;
		Expect( service.TryRemove( Guid.Empty, 1 ), InventoryError.InvalidId );
		Expect( service.TryRemove( Guid.NewGuid(), 1 ), InventoryError.ItemNotFound );
		foreach ( var amount in new[] { 0, -1, int.MinValue } )
			Expect( service.TryRemove( first.Item.InstanceId, amount ), InventoryError.InvalidAmount );
		Require( changes.Count == 2 && inventory.Find( first.Item.InstanceId ) == first.Item );
		Console.WriteLine( "PASS Invalid removals do not mutate or emit events" );

		var partial = service.TryRemove( first.Item.InstanceId, int.MaxValue - 1 );
		Require( partial.Success && partial.Item.Amount == int.MaxValue - 1 );
		Require( inventory.Find( first.Item.InstanceId ).Amount == 1 && first.Item.Amount == int.MaxValue );
		Require( changes[2].Before == first.Item && changes[2].After.Amount == 1 );
		Expect( service.TryRemove( first.Item.InstanceId, 2 ), InventoryError.InsufficientAmount );
		Require( changes.Count == 3 && inventory.Find( first.Item.InstanceId ).Amount == 1 );
		Console.WriteLine( "PASS Partial removal and insufficient quantity are atomic" );

		Expect( service.TryRemove( first.Item.InstanceId, 1 ), InventoryError.None );
		Expect( service.TryRemove( first.Item.InstanceId, 1 ), InventoryError.ItemNotFound );
		Require( inventory.Find( first.Item.InstanceId ) is null && changes[3].After is null );
		Require( snapshot.Count == 2 && snapshot[0].Amount == int.MaxValue );
		Require( inventory.Count( definition ) == int.MaxValue );
		Console.WriteLine( "PASS Full removal, repeated removal and stable snapshots" );

		var properties = new Dictionary<string, string> { ["condition"] = "80" };
		var custom = service.TryAdd( definition, 2, properties ).Item;
		service.TryRemove( custom.InstanceId, 1 );
		Require( inventory.Find( custom.InstanceId ).Properties["condition"] == "80" );
		Console.WriteLine( "PASS Partial removal preserves instance properties" );

		var observed = 0;
		service.Changed += change =>
		{
			Require( inventory.Find( change.After.InstanceId ) == change.After );
			Expect( service.TryAdd( definition, 1 ), InventoryError.OperationInProgress );
			Expect( service.TryRemove( change.After.InstanceId, 1 ), InventoryError.OperationInProgress );
			Expect( service.TryRegisterDefinition( Guid.NewGuid() ), InventoryError.OperationInProgress );
			throw new InvalidOperationException( "Observer failure" );
		};
		service.Changed += _ => observed++;
		var notified = service.TryAdd( definition, 1 );
		Require( notified.Success && notified.NotificationErrors.Count == 1 && observed == 1 );
		Require( notified.NotificationErrors[0] is InvalidOperationException && notified.NotificationErrors[0].Message == "Observer failure" );
		Require( service.TryAdd( definition, 1 ).Success && observed == 2 );
		Console.WriteLine( "PASS Commit before event, reentrancy blocked, observer errors isolated" );

		var other = new InventoryService();
		Expect( other.TryAdd( definition, 1 ), InventoryError.UnknownDefinition );
		Require( other.Inventory.InventoryId != inventory.InventoryId && other.Inventory.Items.Count == 0 );
		Require( inventory.Find( Guid.Empty ) is null && inventory.Count( Guid.NewGuid() ) == 0 );
		Console.WriteLine( "PASS Inventories and catalogs are isolated" );
		return 8;
	}

	private static void Require( bool value )
	{
		if ( !value ) throw new Exception( "Core assertion failed." );
	}
	private static void Expect( InventoryResult result, InventoryError error ) => Require( result.Error == error && result.Success == (error == InventoryError.None) );
}
