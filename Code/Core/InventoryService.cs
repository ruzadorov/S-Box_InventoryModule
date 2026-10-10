using System;
using System.Collections.Generic;

namespace SBox.Inventory;

/// <summary>
/// Single-threaded authority for one inventory. Invoke on the owning game thread.
/// No transport, access, capacity or stacking policies are implied by this core API.
/// </summary>
public sealed class InventoryService
{
	private readonly HashSet<Guid> _definitions = new();
	private bool _notifying;
	private bool _validating;
	internal Func<InventoryChange, InventoryError> ValidateChange { get; set; }
	private readonly Func<Guid> _createId;
	public InventoryService() : this( Guid.NewGuid ) { }
	internal InventoryService( Func<Guid> createId ) => _createId = createId;
	public Inventory Inventory { get; } = new();
	/// <summary>Raised after commit. Reentrant mutations are rejected; each observer is isolated.</summary>
	public event Action<InventoryChange> Changed;

	/// <summary>Registers a validated stable type ID; duplicate IDs are rejected.</summary>
	public InventoryResult TryRegisterDefinition( Guid definitionId )
	{
		if ( _notifying || _validating ) return new( InventoryError.OperationInProgress );
		if ( definitionId == Guid.Empty ) return new( InventoryError.InvalidId );
		return new( _definitions.Add( definitionId ) ? InventoryError.None : InventoryError.DuplicateDefinition );
	}

	/// <summary>Adds a new uniquely identified instance; never implicitly merges items.</summary>
	public InventoryResult TryAdd( Guid definitionId, int amount,
		IReadOnlyDictionary<string, string> properties = null )
	{
		if ( _notifying || _validating ) return new( InventoryError.OperationInProgress );
		if ( definitionId == Guid.Empty ) return new( InventoryError.InvalidId );
		if ( amount <= 0 ) return new( InventoryError.InvalidAmount );
		if ( !_definitions.Contains( definitionId ) ) return new( InventoryError.UnknownDefinition );
		Guid id;
		do { id = _createId(); } while ( id == Guid.Empty || Inventory.Find( id ) is not null );
		ItemInstance item;
		try { item = new ItemInstance( id, definitionId, amount, properties ); }
		catch ( ArgumentException ) { return new( InventoryError.InvalidProperties ); }
		var change = new InventoryChange( null, item );
		var error = Validate( change );
		if ( error != InventoryError.None ) return new( error );
		Inventory.Add( item );
		return Notify( item, change );
	}

	/// <summary>Removes exactly the requested amount, or leaves state untouched.</summary>
	public InventoryResult TryRemove( Guid instanceId, int amount )
	{
		if ( _notifying || _validating ) return new( InventoryError.OperationInProgress );
		if ( instanceId == Guid.Empty ) return new( InventoryError.InvalidId );
		if ( amount <= 0 ) return new( InventoryError.InvalidAmount );
		var before = Inventory.Find( instanceId );
		if ( before is null ) return new( InventoryError.ItemNotFound );
		if ( amount > before.Amount ) return new( InventoryError.InsufficientAmount );
		var removed = new ItemInstance( instanceId, before.DefinitionId, amount, before.Properties );
		var after = amount == before.Amount ? null : new ItemInstance( instanceId, before.DefinitionId, before.Amount - amount, before.Properties );
		var change = new InventoryChange( before, after );
		var error = Validate( change );
		if ( error != InventoryError.None ) return new( error );
		if ( after is null ) Inventory.Remove( instanceId );
		else Inventory.Replace( after );
		return Notify( removed, change );
	}
	private InventoryError Validate( InventoryChange change )
	{
		_validating = true;
		try { return ValidateChange?.Invoke( change ) ?? InventoryError.None; }
		catch ( Exception ) { return InventoryError.RuleFailed; }
		finally { _validating = false; }
	}

	private InventoryResult Notify( ItemInstance item, InventoryChange change )
	{
		var errors = new List<Exception>();
		_notifying = true;
		try
		{
			var observers = Changed;
			if ( observers is not null )
				foreach ( Action<InventoryChange> observer in observers.GetInvocationList() )
				{
					try { observer( change ); }
					catch ( Exception error ) { errors.Add( error ); }
				}
		}
		finally { _notifying = false; }
		return new( InventoryError.None, item, errors.AsReadOnly() );
	}
}
