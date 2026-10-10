using System;
using System.Collections.Generic;

namespace SBox.Inventory;

/// <summary>Explicit outcome of a controlled inventory operation.</summary>
public enum InventoryError
{
	None, InvalidId, InvalidAmount, InvalidProperties, UnknownDefinition,
	DuplicateDefinition, ItemNotFound, InsufficientAmount, OperationInProgress,
	ContainerNotFound, ContainerUnavailable, AccessDenied, AccessPolicyFailed,
	SameContainer, ContainerNotEmpty, DuplicateInstance,
	ContainerCycle, ContainerHasChildren, InvalidContainerReference,
	InvalidRules, RuleFailed, SlotLimitExceeded, QuantityLimitExceeded,
	WeightLimitExceeded, UnknownItemWeight, WeightOverflow, InvalidWeightData,
	ItemTypeNotAllowed, ItemCategoryNotAllowed, UnknownItemCategory,
	StackLimitExceeded, UnknownStackLimit, IncompatibleStacks, QuantityOverflow
}

/// <summary>Failed operations never change state. Observer failures do not undo committed operations.</summary>
public sealed class InventoryResult
{
	public InventoryError Error { get; }
	public bool Success => Error == InventoryError.None;
	/// <summary>Added/transferred snapshot, removed quantity, or resulting destination snapshot after merge.</summary>
	public ItemInstance Item { get; }
	public IReadOnlyList<Exception> NotificationErrors { get; }

	internal InventoryResult( InventoryError error, ItemInstance item = null, IReadOnlyList<Exception> notificationErrors = null )
	{
		Error = error;
		Item = item;
		NotificationErrors = notificationErrors ?? Array.Empty<Exception>();
	}
}
