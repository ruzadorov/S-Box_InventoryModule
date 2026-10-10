namespace SBox.Inventory;

/// <summary>Engine adapter; core only handles validated IDs, never resource objects.</summary>
public static class ItemDefinitionRegistration
{
	public static InventoryResult TryRegisterDefinition( this InventoryService service, ItemDefinition definition )
	{
		if ( definition is null || !definition.TryGetId( out var id ) )
			return new InventoryResult( InventoryError.InvalidId );
		return service.TryRegisterDefinition( id );
	}
}
