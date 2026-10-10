using System;
using Sandbox;

namespace SBox.Inventory;

/// <summary>Editor-authored item type. Instances reference its stable ID, never its asset path.</summary>
[AssetType( Name = "Inventory Item", Extension = "invitem", Category = "Inventory" )]
public sealed class ItemDefinition : GameResource
{
	/// <summary>Explicit, non-empty UUID. Assign once; preserve across renames and moves.</summary>
	public string DefinitionId { get; set; } = "";

	/// <summary>Human-readable name, independent of identity.</summary>
	public string DisplayName { get; set; } = "";

	/// <summary>Checks identity before a definition is admitted to an inventory catalog.</summary>
	public bool TryGetId( out Guid id )
	{
		return Guid.TryParseExact( DefinitionId, "D", out id ) && id != Guid.Empty;
	}
}
