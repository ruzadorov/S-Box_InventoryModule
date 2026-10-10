using System;
using System.Collections.Generic;

namespace SBox.Inventory;

public enum ContainerAction { Add, Remove, TransferOut, TransferIn, Nest, Unnest }

/// <summary>Committed parent relationship change; deletion clears the old relationship.</summary>
public sealed class ContainerHierarchyChange
{
	public Guid ContainerId { get; }
	public Guid? BeforeParentId { get; }
	public Guid? AfterParentId { get; }
	public bool Deleted { get; }
	internal ContainerHierarchyChange( Guid id, Guid? before, Guid? after, bool deleted = false )
	{
		ContainerId = id; BeforeParentId = before; AfterParentId = after; Deleted = deleted;
	}
}

/// <summary>One committed operation. Both sides of a transfer are already visible to observers.</summary>
public sealed class ContainerChange
{
	public Guid ContainerId { get; }
	public InventoryChange Change { get; }
	public Guid? TargetId { get; }
	public InventoryChange TargetChange { get; }
	internal ContainerChange( Guid id, InventoryChange change, Guid? targetId = null, InventoryChange targetChange = null )
	{
		ContainerId = id; Change = change; TargetId = targetId; TargetChange = targetChange;
	}
}

/// <summary>
/// Single-threaded trusted authority for a group of containers. Supply game access policy
/// and an authenticated actor ID; this API is not a network request handler.
/// Create/register/availability/delete are trusted administrative operations.
/// </summary>
public sealed class ContainerService
{
	private readonly Dictionary<Guid, InventoryContainer> _containers = new();
	private readonly HashSet<Guid> _definitions = new();
	private readonly Func<Guid, InventoryContainer, ContainerAction, bool> _access;
	private bool _busy;
	public event Action<ContainerChange> Changed;
	public event Action<ContainerHierarchyChange> HierarchyChanged;

	public ContainerService( Func<Guid, InventoryContainer, ContainerAction, bool> access )
	{
		_access = access ?? throw new ArgumentNullException( nameof( access ) );
	}

	/// <summary>Creates exclusively owned state. Never adopts containers owned by other services.</summary>
	public InventoryContainer Create( ContainerDefinition definition )
	{
		if ( _busy ) throw new InvalidOperationException( "Operation in progress." );
		var container = new InventoryContainer( definition, NewInstanceId );
		foreach ( var id in _definitions ) container.Operations.TryRegisterDefinition( id );
		_containers.Add( container.ContainerId, container );
		return container;
	}
	public InventoryContainer Find( Guid id ) => _containers.TryGetValue( id, out var container ) ? container : null;
	/// <summary>Read-only direct-child snapshot. Unknown IDs return an empty list.</summary>
	public IReadOnlyList<InventoryContainer> GetChildren( Guid id )
	{
		var children = new List<InventoryContainer>();
		foreach ( var container in _containers.Values )
			if ( container.ParentId == id ) children.Add( container );
		return children.AsReadOnly();
	}

	/// <summary>
	/// Atomically reparents storage, never item data. Checks child, old parent and new
	/// parent access plus ancestors; null detaches to root. No-op emits no event.
	/// </summary>
	public InventoryResult TrySetParent( Guid actorId, Guid childId, Guid? parentId ) => Execute( () =>
	{
		if ( parentId == Guid.Empty ) return new( InventoryError.InvalidId );
		if ( childId == parentId ) return new( InventoryError.ContainerCycle );
		var error = Authorize( actorId, childId, parentId.HasValue ? ContainerAction.Nest : ContainerAction.Unnest, out var child );
		if ( error != InventoryError.None ) return new( error );
		if ( parentId.HasValue )
		{
			error = Authorize( actorId, parentId.Value, ContainerAction.Nest, out var parent );
			if ( error != InventoryError.None ) return new( error );
			var seen = new HashSet<Guid>();
			while ( parent is not null )
			{
				if ( parent.ContainerId == childId ) return new( InventoryError.ContainerCycle );
				if ( !seen.Add( parent.ContainerId ) ) return new( InventoryError.InvalidContainerReference );
				if ( !parent.ParentId.HasValue ) break;
				parent = Find( parent.ParentId.Value );
				if ( parent is null ) return new( InventoryError.InvalidContainerReference );
			}
		}
		if ( child.ParentId == parentId ) return new( InventoryError.None );
		if ( child.ParentId.HasValue )
		{
			error = Authorize( actorId, child.ParentId.Value, ContainerAction.Unnest, out _ );
			if ( error != InventoryError.None ) return new( error );
		}
		var change = new ContainerHierarchyChange( childId, child.ParentId, parentId );
		child.SetParent( parentId );
		return NotifyHierarchy( change );
	} );

	public InventoryResult TryRegisterDefinition( Guid id ) => Execute( () =>
	{
		if ( id == Guid.Empty ) return new( InventoryError.InvalidId );
		if ( !_definitions.Add( id ) ) return new( InventoryError.DuplicateDefinition );
		foreach ( var container in _containers.Values ) container.Operations.TryRegisterDefinition( id );
		return new( InventoryError.None );
	} );

	public InventoryResult TrySetAvailable( Guid id, bool available ) => Execute( () =>
	{
		var error = Resolve( id, out var container );
		if ( error != InventoryError.None ) return new( error );
		container.SetAvailable( available );
		return new( InventoryError.None );
	} );

	/// <summary>Trusted configuration. Rejects rules incompatible with existing contents.</summary>
	public InventoryResult TrySetRules( Guid id, IReadOnlyList<IInventoryRule> rules ) => Execute( () =>
	{
		var error = Resolve( id, out var container );
		if ( error != InventoryError.None ) return new( error );
		InventoryRuleSet candidate;
		try { candidate = new InventoryRuleSet( rules ); }
		catch ( ArgumentException ) { return new( InventoryError.InvalidRules ); }
		error = candidate.Validate( new InventoryRuleContext( container.Inventory, null ) );
		if ( error != InventoryError.None ) return new( error );
		container.SetRules( candidate );
		return new( InventoryError.None );
	} );

	/// <summary>Rejects deletion of nonempty storage. Drain explicitly first; no implicit item destruction.</summary>
	public InventoryResult TryDelete( Guid id ) => Execute( () =>
	{
		var error = Resolve( id, out var container );
		if ( error != InventoryError.None ) return new( error );
		if ( container.Inventory.Items.Count != 0 ) return new( InventoryError.ContainerNotEmpty );
		if ( GetChildren( id ).Count != 0 ) return new( InventoryError.ContainerHasChildren );
		var change = new ContainerHierarchyChange( id, container.ParentId, null, true );
		_containers.Remove( id );
		container.SetAvailable( false );
		container.SetParent( null );
		return NotifyHierarchy( change );
	} );

	public InventoryResult TryAdd( Guid actorId, Guid containerId, Guid definitionId, int amount,
		IReadOnlyDictionary<string, string> properties = null ) => Execute( () =>
	{
		var error = Authorize( actorId, containerId, ContainerAction.Add, out var container );
		if ( error != InventoryError.None ) return new( error );
		var result = container.Operations.TryAdd( definitionId, amount, properties );
		return result.Success ? Notify( result.Item, new( containerId, new( null, result.Item ) ) ) : result;
	} );

	public InventoryResult TryRemove( Guid actorId, Guid containerId, Guid itemId, int amount ) => Execute( () =>
	{
		var error = Authorize( actorId, containerId, ContainerAction.Remove, out var container );
		if ( error != InventoryError.None ) return new( error );
		var before = container.Inventory.Find( itemId );
		var result = container.Operations.TryRemove( itemId, amount );
		return result.Success ? Notify( result.Item, new( containerId, new( before, container.Inventory.Find( itemId ) ) ) ) : result;
	} );

	/// <summary>
	/// Exact atomic transfer, with no merging. Full transfer preserves identity;
	/// partial transfer creates a new identity for the destination portion.
	/// All validation and snapshot allocation precede state changes and notifications.
	/// </summary>
	public InventoryResult TryTransfer( Guid actorId, Guid sourceId, Guid targetId, Guid itemId, int amount ) => Execute( () =>
	{
		if ( sourceId == Guid.Empty || targetId == Guid.Empty || itemId == Guid.Empty ) return new( InventoryError.InvalidId );
		if ( sourceId == targetId ) return new( InventoryError.SameContainer );
		if ( amount <= 0 ) return new( InventoryError.InvalidAmount );
		var error = Authorize( actorId, sourceId, ContainerAction.TransferOut, out var source );
		if ( error != InventoryError.None ) return new( error );
		error = Authorize( actorId, targetId, ContainerAction.TransferIn, out var target );
		if ( error != InventoryError.None ) return new( error );
		var before = source.Inventory.Find( itemId );
		if ( before is null ) return new( InventoryError.ItemNotFound );
		if ( amount > before.Amount ) return new( InventoryError.InsufficientAmount );
		if ( target.Inventory.Find( itemId ) is not null ) return new( InventoryError.DuplicateInstance );
		var full = amount == before.Amount;
		var transferred = full ? before : new ItemInstance( NewInstanceId(), before.DefinitionId, amount, before.Properties );
		var remainder = full ? null : new ItemInstance( itemId, before.DefinitionId, before.Amount - amount, before.Properties );
		var sourceChange = new InventoryChange( before, remainder );
		var targetChange = new InventoryChange( null, transferred );
		error = source.Rules.Validate( new InventoryRuleContext( source.Inventory, sourceChange ) );
		if ( error != InventoryError.None ) return new( error );
		error = target.Rules.Validate( new InventoryRuleContext( target.Inventory, targetChange ) );
		if ( error != InventoryError.None ) return new( error );
		// Destination insertion occurs first: an insertion failure cannot lose source data.
		target.Inventory.Add( transferred );
		if ( full ) source.Inventory.Remove( itemId );
		else source.Inventory.Replace( remainder );
		return Notify( transferred, new( sourceId, sourceChange, targetId, targetChange ) );
	} );

	/// <summary>
	/// Exact atomic merge, within one container or between two. Types and ordinal
	/// property keys/values must match. Destination identity survives; a fully
	/// consumed source identity disappears. No partial success or automatic merge.
	/// </summary>
	public InventoryResult TryMergeStacks( Guid actorId, Guid sourceId, Guid targetId,
		Guid sourceItemId, Guid targetItemId, int amount ) => Execute( () =>
	{
		if ( sourceItemId == Guid.Empty || targetItemId == Guid.Empty ) return new( InventoryError.InvalidId );
		if ( sourceItemId == targetItemId ) return new( InventoryError.IncompatibleStacks );
		if ( amount <= 0 ) return new( InventoryError.InvalidAmount );
		var error = Authorize( actorId, sourceId, ContainerAction.TransferOut, out var source );
		if ( error != InventoryError.None ) return new( error );
		error = Authorize( actorId, targetId, ContainerAction.TransferIn, out var target );
		if ( error != InventoryError.None ) return new( error );
		var before = source.Inventory.Find( sourceItemId );
		var destination = target.Inventory.Find( targetItemId );
		if ( before is null || destination is null ) return new( InventoryError.ItemNotFound );
		if ( amount > before.Amount ) return new( InventoryError.InsufficientAmount );
		if ( !Compatible( before, destination ) ) return new( InventoryError.IncompatibleStacks );
		if ( amount > int.MaxValue - destination.Amount ) return new( InventoryError.QuantityOverflow );
		var remainder = amount == before.Amount ? null :
			new ItemInstance( sourceItemId, before.DefinitionId, before.Amount - amount, before.Properties );
		var merged = new ItemInstance( targetItemId, destination.DefinitionId, destination.Amount + amount, destination.Properties );
		var sourceChange = new InventoryChange( before, remainder );
		var targetChange = new InventoryChange( destination, merged );
		if ( sourceId == targetId )
			error = source.Rules.Validate( new InventoryRuleContext( source.Inventory, sourceChange, targetChange ) );
		else
		{
			error = source.Rules.Validate( new InventoryRuleContext( source.Inventory, sourceChange ) );
			if ( error == InventoryError.None )
				error = target.Rules.Validate( new InventoryRuleContext( target.Inventory, targetChange ) );
		}
		if ( error != InventoryError.None ) return new( error );
		target.Inventory.Replace( merged );
		if ( remainder is null ) source.Inventory.Remove( sourceItemId );
		else source.Inventory.Replace( remainder );
		return Notify( merged, new( sourceId, sourceChange, targetId, targetChange ) );
	} );

	private static bool Compatible( ItemInstance source, ItemInstance target )
	{
		if ( source.DefinitionId != target.DefinitionId || source.Properties.Count != target.Properties.Count ) return false;
		foreach ( var pair in source.Properties )
			if ( !target.Properties.TryGetValue( pair.Key, out var value ) || !string.Equals( pair.Value, value, StringComparison.Ordinal ) ) return false;
		return true;
	}

	private Guid NewInstanceId()
	{
		while ( true )
		{
			var id = Guid.NewGuid();
			if ( id == Guid.Empty ) continue;
			bool found = false;
			foreach ( var container in _containers.Values )
				if ( container.Inventory.Find( id ) is not null ) { found = true; break; }
			if ( !found ) return id;
		}
	}
	private InventoryError Resolve( Guid id, out InventoryContainer container )
	{
		container = Find( id );
		return id == Guid.Empty ? InventoryError.InvalidId : container is null ? InventoryError.ContainerNotFound : InventoryError.None;
	}
	private InventoryError Authorize( Guid actor, Guid id, ContainerAction action, out InventoryContainer container )
	{
		var error = Resolve( id, out container );
		if ( error != InventoryError.None ) return error;
		if ( actor == Guid.Empty ) return InventoryError.InvalidId;
		var current = container;
		var seen = new HashSet<Guid>();
		while ( current is not null )
		{
			if ( !seen.Add( current.ContainerId ) ) return InventoryError.InvalidContainerReference;
			if ( !current.IsAvailable ) return InventoryError.ContainerUnavailable;
			try { if ( !_access( actor, current, action ) ) return InventoryError.AccessDenied; }
			catch ( Exception ) { return InventoryError.AccessPolicyFailed; }
			if ( !current.ParentId.HasValue ) break;
			current = Find( current.ParentId.Value );
			if ( current is null ) return InventoryError.InvalidContainerReference;
		}
		return InventoryError.None;
	}
	private InventoryResult Execute( Func<InventoryResult> operation )
	{
		if ( _busy ) return new( InventoryError.OperationInProgress );
		_busy = true;
		try { return operation(); }
		finally { _busy = false; }
	}
	private InventoryResult Notify( ItemInstance item, ContainerChange change )
	{
		var errors = new List<Exception>();
		var observers = Changed;
		if ( observers is not null )
			foreach ( Action<ContainerChange> observer in observers.GetInvocationList() )
			{
				try { observer( change ); }
				catch ( Exception error ) { errors.Add( error ); }
			}
		return new( InventoryError.None, item, errors.AsReadOnly() );
	}
	private InventoryResult NotifyHierarchy( ContainerHierarchyChange change )
	{
		var errors = new List<Exception>();
		var observers = HierarchyChanged;
		if ( observers is not null )
			foreach ( Action<ContainerHierarchyChange> observer in observers.GetInvocationList() )
			{
				try { observer( change ); }
				catch ( Exception error ) { errors.Add( error ); }
			}
		return new( InventoryError.None, notificationErrors: errors.AsReadOnly() );
	}
}
