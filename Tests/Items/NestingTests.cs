using SBox.Inventory;

internal static class NestingTests
{
	public static int Run()
	{
		var actor = Guid.NewGuid();
		var type = Guid.NewGuid();
		Guid? deniedId = null;
		ContainerAction? deniedAction = null;
		var service = new ContainerService( (who, container, action) =>
			who == actor && !(container.ContainerId == deniedId && (deniedAction is null || action == deniedAction)) );
		service.TryRegisterDefinition( type );
		var definition = new ContainerDefinition( Guid.NewGuid(), "Bag" );
		var root = service.Create( definition );
		var bag = service.Create( definition );
		var pocket = service.Create( definition );
		var other = service.Create( definition );
		var events = new List<ContainerHierarchyChange>();
		service.HierarchyChanged += events.Add;
		Expect( service.TrySetParent( actor, bag.ContainerId, root.ContainerId ), InventoryError.None );
		var children = service.GetChildren( root.ContainerId );
		Expect( service.TrySetParent( actor, pocket.ContainerId, bag.ContainerId ), InventoryError.None );
		Require( bag.ParentId == root.ContainerId && pocket.ParentId == bag.ContainerId );
		Require( children.Count == 1 && children[0] == bag && events.Count == 2 );
		Require( events[0].BeforeParentId is null && events[0].AfterParentId == root.ContainerId );
		Console.WriteLine( "PASS Nested tree and committed hierarchy snapshots" );

		Expect( service.TrySetParent( actor, root.ContainerId, root.ContainerId ), InventoryError.ContainerCycle );
		Expect( service.TrySetParent( actor, root.ContainerId, pocket.ContainerId ), InventoryError.ContainerCycle );
		Expect( service.TrySetParent( actor, bag.ContainerId, pocket.ContainerId ), InventoryError.ContainerCycle );
		Expect( service.TrySetParent( actor, bag.ContainerId, Guid.Empty ), InventoryError.InvalidId );
		Expect( service.TrySetParent( actor, bag.ContainerId, Guid.NewGuid() ), InventoryError.ContainerNotFound );
		Expect( service.TrySetParent( actor, Guid.NewGuid(), root.ContainerId ), InventoryError.ContainerNotFound );
		Require( events.Count == 2 && root.ParentId is null && bag.ParentId == root.ContainerId );
		Console.WriteLine( "PASS Self, indirect cycles and unknown references rejected atomically" );

		Expect( service.TrySetParent( actor, bag.ContainerId, root.ContainerId ), InventoryError.None );
		Require( events.Count == 2 );
		deniedId = root.ContainerId; deniedAction = ContainerAction.Unnest;
		Expect( service.TrySetParent( actor, bag.ContainerId, other.ContainerId ), InventoryError.AccessDenied );
		deniedId = other.ContainerId; deniedAction = ContainerAction.Nest;
		Expect( service.TrySetParent( actor, bag.ContainerId, other.ContainerId ), InventoryError.AccessDenied );
		deniedId = null;
		Expect( service.TrySetParent( actor, bag.ContainerId, other.ContainerId ), InventoryError.None );
		Require( service.GetChildren( root.ContainerId ).Count == 0 && service.GetChildren( other.ContainerId ).Count == 1 );
		Require( pocket.ParentId == bag.ContainerId && children.Count == 1 );
		Console.WriteLine( "PASS Reparent validates both parents and keeps descendants intact" );

		deniedId = other.ContainerId; deniedAction = null;
		Expect( service.TryAdd( actor, pocket.ContainerId, type, 1 ), InventoryError.AccessDenied );
		Expect( service.TrySetParent( actor, pocket.ContainerId, null ), InventoryError.AccessDenied );
		deniedId = null;
		service.TrySetAvailable( other.ContainerId, false );
		Expect( service.TryAdd( actor, pocket.ContainerId, type, 1 ), InventoryError.ContainerUnavailable );
		service.TrySetAvailable( other.ContainerId, true );
		var item = service.TryAdd( actor, pocket.ContainerId, type, 3 ).Item;
		deniedId = other.ContainerId;
		Expect( service.TryRemove( actor, pocket.ContainerId, item.InstanceId, 1 ), InventoryError.AccessDenied );
		Expect( service.TryTransfer( actor, pocket.ContainerId, root.ContainerId, item.InstanceId, 1 ), InventoryError.AccessDenied );
		Expect( service.TryTransfer( actor, root.ContainerId, pocket.ContainerId, Guid.NewGuid(), 1 ), InventoryError.AccessDenied );
		deniedId = null;
		Require( pocket.Inventory.Count( type ) == 3 && root.Inventory.Count( type ) == 0 );
		Console.WriteLine( "PASS Ancestor access and availability apply to content and detach operations" );

		Expect( service.TryDelete( bag.ContainerId ), InventoryError.ContainerHasChildren );
		Expect( service.TryDelete( other.ContainerId ), InventoryError.ContainerHasChildren );
		Expect( service.TryDelete( pocket.ContainerId ), InventoryError.ContainerNotEmpty );
		service.TryRemove( actor, pocket.ContainerId, item.InstanceId, 3 );
		Expect( service.TryDelete( pocket.ContainerId ), InventoryError.None );
		Require( pocket.ParentId is null && !pocket.IsAvailable && service.GetChildren( bag.ContainerId ).Count == 0 );
		Require( events[^1].Deleted && events[^1].BeforeParentId == bag.ContainerId );
		Expect( service.TrySetParent( actor, bag.ContainerId, pocket.ContainerId ), InventoryError.ContainerNotFound );
		Expect( service.TrySetParent( actor, bag.ContainerId, null ), InventoryError.None );
		Require( service.GetChildren( other.ContainerId ).Count == 0 );
		Expect( service.TryDelete( other.ContainerId ), InventoryError.None );
		Console.WriteLine( "PASS Nested deletion prevents dangling children and clears retained parent references" );

		// Deliberate internal corruption to exercise defensive traversal, not a public API.
		bag.SetParent( Guid.NewGuid() );
		Expect( service.TryAdd( actor, bag.ContainerId, type, 1 ), InventoryError.InvalidContainerReference );
		bag.SetParent( root.ContainerId ); root.SetParent( bag.ContainerId );
		Expect( service.TryAdd( actor, bag.ContainerId, type, 1 ), InventoryError.InvalidContainerReference );
		root.SetParent( null ); bag.SetParent( null );
		Console.WriteLine( "PASS Broken and cyclic internal references fail closed without infinite traversal" );

		var branch = root;
		for ( var i = 0; i < 128; i++ )
		{
			var child = service.Create( definition );
			Expect( service.TrySetParent( actor, child.ContainerId, branch.ContainerId ), InventoryError.None );
			branch = child;
		}
		Require( service.TryAdd( actor, branch.ContainerId, type, 1 ).Success );
		Expect( service.TrySetParent( actor, root.ContainerId, branch.ContainerId ), InventoryError.ContainerCycle );
		Console.WriteLine( "PASS Deep hierarchy traversed iteratively with cycle detection" );

		int observed = 0;
		service.HierarchyChanged += change =>
		{
			Require( bag.ParentId == root.ContainerId );
			Expect( service.TryDelete( bag.ContainerId ), InventoryError.OperationInProgress );
			Expect( service.TrySetParent( actor, bag.ContainerId, null ), InventoryError.OperationInProgress );
			throw new InvalidOperationException( "Hierarchy observer failure" );
		};
		service.HierarchyChanged += _ => observed++;
		var result = service.TrySetParent( actor, bag.ContainerId, root.ContainerId );
		Require( result.Success && observed == 1 && result.NotificationErrors.Count == 1 );
		Require( result.NotificationErrors[0].Message == "Hierarchy observer failure" );
		Console.WriteLine( "PASS Hierarchy events isolate observer failures and reject reentrant mutations" );
		return 8;
	}
	private static void Require( bool condition )
	{
		if ( !condition ) throw new Exception( "Nesting assertion failed." );
	}
	private static void Expect( InventoryResult result, InventoryError error ) => Require( result.Error == error );
}
