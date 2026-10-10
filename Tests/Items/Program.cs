using SBox.Inventory;

var tests = new (string Name, Action Run)[]
{
	("Snapshot preserves identity and quantity", () =>
	{
		var id = Guid.NewGuid();
		var definition = Guid.NewGuid();
		var item = new ItemInstance( id, definition, 3 );
		Check( item.InstanceId == id && item.DefinitionId == definition && item.Amount == 3 );
	}),
	("Empty IDs rejected", () =>
	{
		Throws<ArgumentException>( () => new ItemInstance( Guid.Empty, Guid.NewGuid(), 1 ) );
		Throws<ArgumentException>( () => new ItemInstance( Guid.NewGuid(), Guid.Empty, 1 ) );
	}),
	("Non-positive quantities rejected", () =>
	{
		foreach ( var amount in new[] { 0, -1, int.MinValue } )
			Throws<ArgumentOutOfRangeException>( () => Create( amount ) );
	}),
	("Maximum quantity preserved without overflow", () => Check( Create( int.MaxValue ).Amount == int.MaxValue )),
	("Properties copied and read-only", () =>
	{
		var source = new Dictionary<string, string> { ["condition"] = "80" };
		var item = new ItemInstance( Guid.NewGuid(), Guid.NewGuid(), 1, source );
		source["condition"] = "0";
		Check( item.Properties["condition"] == "80" );
		Throws<NotSupportedException>( () => ((IDictionary<string, string>)item.Properties).Add( "new", "value" ) );
	}),
	("Invalid properties rejected", () =>
	{
		foreach ( var key in new[] { "", " " } )
			Throws<ArgumentException>( () => new ItemInstance( Guid.NewGuid(), Guid.NewGuid(), 1, new Dictionary<string, string> { [key] = "x" } ) );
		Throws<ArgumentException>( () => new ItemInstance( Guid.NewGuid(), Guid.NewGuid(), 1, new Dictionary<string, string> { ["x"] = null } ) );
	}),
	("Properties use ordinal identity", () =>
	{
		var item = new ItemInstance( Guid.NewGuid(), Guid.NewGuid(), 1, new Dictionary<string, string> { ["x"] = "1", ["X"] = "2" } );
		Check( item.Properties.Count == 2 );
	}),
	("Default properties empty", () => Check( Create( 1 ).Properties.Count == 0 ))
};

foreach ( var test in tests )
{
	test.Run();
	Console.WriteLine( $"PASS {test.Name}" );
}
Console.WriteLine( $"Passed {tests.Length + CoreTests.Run() + ContainerTests.Run() + TransferTests.Run() + NestingTests.Run() + RuleTests.Run() + WeightTests.Run() + FilterTests.Run() + StackTests.Run()} test groups." );

static ItemInstance Create( int amount ) => new( Guid.NewGuid(), Guid.NewGuid(), amount );
static void Check( bool condition )
{
	if ( !condition ) throw new Exception( "Assertion failed." );
}
static void Throws<T>( Action action ) where T : Exception
{
	try { action(); }
	catch ( T ) { return; }
	throw new Exception( $"Expected {typeof( T ).Name}." );
}
