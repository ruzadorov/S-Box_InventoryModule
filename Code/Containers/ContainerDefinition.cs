using System;

namespace SBox.Inventory;

/// <summary>
/// Immutable description of a container type, shared by player, world and bag containers.
/// Capacity and filtering belong to the upcoming Rules module.
/// </summary>
public sealed class ContainerDefinition
{
	/// <summary>Stable, authored type identity; independent of a scene or object name.</summary>
	public Guid DefinitionId { get; }
	public string DisplayName { get; }

	public ContainerDefinition( Guid definitionId, string displayName )
	{
		if ( definitionId == Guid.Empty )
			throw new ArgumentException( "Container definition ID must not be empty.", nameof( definitionId ) );
		if ( string.IsNullOrWhiteSpace( displayName ) )
			throw new ArgumentException( "Container display name must not be blank.", nameof( displayName ) );
		DefinitionId = definitionId;
		DisplayName = displayName;
	}
}
