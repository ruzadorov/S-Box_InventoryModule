using System;
using System.Text.Json.Serialization;
using Sandbox;

namespace SBox.Inventory;

/// <summary>
/// GameObject adapter for externally owned container data. Binding is explicit;
/// destroying the component detaches the view and never deletes inventory contents.
/// </summary>
public sealed class InventoryComponent : Component
{
	/// <summary>Runtime binding only; scenes do not serialize domain state.</summary>
	[JsonIgnore]
	public InventoryContainer Container { get; private set; }

	/// <summary>Attaches existing data. Detach explicitly before binding a different container.</summary>
	public void Bind( InventoryContainer container )
	{
		if ( !IsValid )
			throw new InvalidOperationException( "Cannot bind a destroyed or unattached component." );
		if ( container is null ) throw new ArgumentNullException( nameof( container ) );
		if ( Container is not null && !ReferenceEquals( Container, container ) )
			throw new InvalidOperationException( "Detach the current container before rebinding." );
		Container = container;
	}

	/// <summary>Releases this scene reference without modifying externally owned data.</summary>
	public void Unbind() => Container = null;

	protected override void OnDestroy()
	{
		Unbind();
	}
}
