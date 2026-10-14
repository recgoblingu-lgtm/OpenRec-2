using System.Collections.Concurrent;

namespace OpenRec_2;

public sealed record InventoryItem(string ItemId, string Name, string Category, int Quantity, bool Equipped, IReadOnlyDictionary<string, string> Metadata, DateTimeOffset UpdatedAt);
public sealed record InventoryGrantRequest(Guid UserId, string ItemId, string Name, string Category, int Quantity, Dictionary<string, string>? Metadata);
public sealed record InventoryQuantityRequest(int Quantity);
public sealed record InventoryEquipRequest(bool Equipped);

public sealed class InventoryService
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, InventoryEntry>> _inventories = new();

    public IReadOnlyList<InventoryItem> List(Guid userId)
    {
        if (!_inventories.TryGetValue(userId, out var inventory))
        {
            return [];
        }
        return inventory.Values.OrderBy(item => item.Category).ThenBy(item => item.Name).Select(ToItem).ToArray();
    }

    public InventoryItem? Grant(InventoryGrantRequest request)
    {
        var itemId = request.ItemId.Trim();
        var name = request.Name.Trim();
        var category = request.Category.Trim();
        if (itemId.Length is < 1 or > 64 || name.Length is < 1 or > 128 || category.Length is < 1 or > 32 || request.Quantity is < 1 or > 100000)
        {
            return null;
        }

        var inventory = _inventories.GetOrAdd(request.UserId, _ => new());
        var entry = inventory.AddOrUpdate(itemId,
            _ => new InventoryEntry(itemId, name, category, request.Quantity, false, request.Metadata ?? [], DateTimeOffset.UtcNow),
            (_, current) => current with
            {
                Name = name,
                Category = category,
                Quantity = Math.Min(100000, current.Quantity + request.Quantity),
                Metadata = request.Metadata ?? current.Metadata,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        return ToItem(entry);
    }

    public InventoryItem? SetQuantity(Guid userId, string itemId, int quantity)
    {
        if (!_inventories.TryGetValue(userId, out var inventory) || !inventory.TryGetValue(itemId, out var current) || quantity < 0 || quantity > 100000)
        {
            return null;
        }
        if (quantity == 0)
        {
            inventory.TryRemove(itemId, out _);
            return null;
        }
        var updated = current with { Quantity = quantity, Equipped = current.Equipped && quantity > 0, UpdatedAt = DateTimeOffset.UtcNow };
        inventory[itemId] = updated;
        return ToItem(updated);
    }

    public InventoryItem? SetEquipped(Guid userId, string itemId, bool equipped)
    {
        if (!_inventories.TryGetValue(userId, out var inventory) || !inventory.TryGetValue(itemId, out var current) || current.Quantity < 1)
        {
            return null;
        }
        var updated = current with { Equipped = equipped, UpdatedAt = DateTimeOffset.UtcNow };
        inventory[itemId] = updated;
        return ToItem(updated);
    }

    public int Count(Guid userId) => _inventories.TryGetValue(userId, out var inventory) ? inventory.Count : 0;

    private static InventoryItem ToItem(InventoryEntry entry) => new(entry.ItemId, entry.Name, entry.Category, entry.Quantity, entry.Equipped, entry.Metadata, entry.UpdatedAt);

    private sealed record InventoryEntry(string ItemId, string Name, string Category, int Quantity, bool Equipped, IReadOnlyDictionary<string, string> Metadata, DateTimeOffset UpdatedAt);
}
