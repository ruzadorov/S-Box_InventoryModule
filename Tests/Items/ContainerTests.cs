using SBox.Inventory;

internal static class ContainerTests
{
	public static int Run()
	{
		Throws<ArgumentException>( () => new ContainerDefinition( Guid.Empty, "Bag" ) );
		foreach ( var name in new[] { null, "", " " } )
			Throws<ArgumentException>( () => new ContainerDefinition( Guid.NewGuid(), name ) );
		Throws<ArgumentNullException>( () => new InventoryContainer( null ) );
		Console.WriteLine( "PASS Invalid container definitions rejected" );

		var id = Guid.NewGuid();
		var definition = new ContainerDefinition( id, "Bag" );
		var first = new InventoryContainer( definition );
		var second = new InventoryContainer( definition );
		Require( first.Definition == definition && second.Definition == definition );
		Require( definition.DefinitionId == id && definition.DisplayName == "Bag" );
		Require( first.ContainerId != Guid.Empty && first.ContainerId != second.ContainerId );
		Require( first.ContainerId == first.Inventory.InventoryId );
		Require( first.Inventory.Items.Count == 0 && second.Inventory.Items.Count == 0 );
		Console.WriteLine( "PASS Shared definition and independent container identities" );

		// Test-only access to internal operations; public consumers receive only the view.
		var type = Guid.NewGuid();
		first.Operations.TryRegisterDefinition( type );
		var added = first.Operations.TryAdd( type, 3 );
		Require( added.Success && first.Inventory.Count( type ) == 3 );
		Require( second.Inventory.Count( type ) == 0 );
		Require( second.Operations.TryAdd( type, 1 ).Error == InventoryError.UnknownDefinition );
		Require( first.Inventory.Find( added.Item.InstanceId ) == added.Item );
		Console.WriteLine( "PASS Container contents and catalogs remain isolated" );

		Require( typeof( InventoryContainer ).GetProperties().All( p => p.SetMethod is null ) );
		Require( typeof( ContainerDefinition ).GetProperties().All( p => p.SetMethod is null ) );
		Require( typeof( InventoryContainer ).GetProperties().All( p => p.PropertyType != typeof( InventoryService ) ) );
		Console.WriteLine( "PASS Public container contracts expose read-only state" );
		return 4;
	}
	private static void Require( bool condition )
	{
		if ( !condition ) throw new Exception( "Container assertion failed." );
	}
	private static void Throws<T>( Action action ) where T : Exception
	{
		try { action(); }
		catch ( T ) { return; }
		throw new Exception( $"Expected {typeof( T ).Name}." );
	}
}
