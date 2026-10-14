using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace OpenRec_2;

public sealed record AvatarProfile(
    Guid UserId,
    string BodyColor,
    string SkinMaterial,
    string HairStyle,
    string EyeColor,
    string FaceStyle,
    double HeightScale,
    double HeadScale,
    DateTimeOffset UpdatedAt);

public sealed record UpdateAvatarRequest(
    string? BodyColor,
    string? SkinMaterial,
    string? HairStyle,
    string? EyeColor,
    string? FaceStyle,
    double? HeightScale,
    double? HeadScale);

public sealed record ClothingItem(
    string ItemId,
    string Name,
    string Slot,
    string AssetId,
    bool Equipped,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset GrantedAt);

public sealed record GrantClothingRequest(
    Guid UserId,
    string ItemId,
    string Name,
    string Slot,
    string AssetId,
    Dictionary<string, string>? Metadata);

public sealed class AvatarService
{
    private static readonly Regex ColorPattern = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);
    private readonly ConcurrentDictionary<Guid, AvatarProfile> _profiles = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, ClothingEntry>> _wardrobes = new();

    public AvatarProfile GetProfile(Guid userId)
    {
        return _profiles.GetOrAdd(userId, id => new AvatarProfile(id, "#D99B72", "default", "default", "#4A3428", "default", 1, 1, DateTimeOffset.UtcNow));
    }

    public AvatarProfile? UpdateProfile(Guid userId, UpdateAvatarRequest request)
    {
        var current = GetProfile(userId);
        var bodyColor = NormalizeColor(request.BodyColor, current.BodyColor);
        var eyeColor = NormalizeColor(request.EyeColor, current.EyeColor);
        if (bodyColor is null || eyeColor is null)
        {
            return null;
        }

        var updated = current with
        {
            BodyColor = bodyColor,
            EyeColor = eyeColor,
            SkinMaterial = NormalizeText(request.SkinMaterial, current.SkinMaterial, 32),
            HairStyle = NormalizeText(request.HairStyle, current.HairStyle, 64),
            FaceStyle = NormalizeText(request.FaceStyle, current.FaceStyle, 64),
            HeightScale = NormalizeScale(request.HeightScale, current.HeightScale),
            HeadScale = NormalizeScale(request.HeadScale, current.HeadScale),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _profiles[userId] = updated;
        return updated;
    }

    public IReadOnlyList<ClothingItem> ListClothing(Guid userId)
    {
        return _wardrobes.TryGetValue(userId, out var wardrobe)
            ? wardrobe.Values.OrderBy(item => item.Slot).ThenBy(item => item.Name).Select(ToItem).ToArray()
            : [];
    }

    public ClothingItem? GrantClothing(GrantClothingRequest request)
    {
        var itemId = request.ItemId.Trim();
        var name = request.Name.Trim();
        var slot = request.Slot.Trim().ToLowerInvariant();
        var assetId = request.AssetId.Trim();
        if (itemId.Length is < 1 or > 64 || name.Length is < 1 or > 128 || slot.Length is < 1 or > 32 || assetId.Length is < 1 or > 128)
        {
            return null;
        }

        var wardrobe = _wardrobes.GetOrAdd(request.UserId, _ => new());
        var item = wardrobe.AddOrUpdate(itemId,
            _ => new ClothingEntry(itemId, name, slot, assetId, false, request.Metadata ?? [], DateTimeOffset.UtcNow),
            (_, current) => current with { Name = name, Slot = slot, AssetId = assetId, Metadata = request.Metadata ?? current.Metadata });
        return ToItem(item);
    }

    public ClothingItem? SetEquipped(Guid userId, string itemId, bool equipped)
    {
        if (!_wardrobes.TryGetValue(userId, out var wardrobe) || !wardrobe.TryGetValue(itemId, out var item))
        {
            return null;
        }

        if (equipped)
        {
            foreach (var other in wardrobe.Values.Where(other => other.Slot == item.Slot && other.ItemId != item.ItemId))
            {
                wardrobe[other.ItemId] = other with { Equipped = false };
            }
        }
        var updated = item with { Equipped = equipped };
        wardrobe[itemId] = updated;
        return ToItem(updated);
    }

    public bool RemoveClothing(Guid userId, string itemId)
    {
        return _wardrobes.TryGetValue(userId, out var wardrobe) && wardrobe.TryRemove(itemId, out _);
    }

    private static string? NormalizeColor(string? value, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return ColorPattern.IsMatch(normalized) ? normalized.ToUpperInvariant() : null;
    }

    private static string NormalizeText(string? value, string fallback, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return normalized[..Math.Min(normalized.Length, maxLength)];
    }

    private static double NormalizeScale(double? value, double fallback)
    {
        return value is null ? fallback : Math.Clamp(value.Value, 0.5, 2.0);
    }

    private static ClothingItem ToItem(ClothingEntry item) => new(item.ItemId, item.Name, item.Slot, item.AssetId, item.Equipped, item.Metadata, item.GrantedAt);

    private sealed record ClothingEntry(string ItemId, string Name, string Slot, string AssetId, bool Equipped, IReadOnlyDictionary<string, string> Metadata, DateTimeOffset GrantedAt);
}
