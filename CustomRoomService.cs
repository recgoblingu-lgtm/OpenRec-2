using System.Collections.Concurrent;

namespace OpenRec_2;

public sealed record RoomCustomization(
    Guid RoomId,
    string DisplayName,
    string Description,
    string Theme,
    string[] Tags,
    string? WelcomeMessage,
    int Revision,
    DateTimeOffset UpdatedAt,
    Guid UpdatedBy);

public sealed record CustomizeRoomRequest(string DisplayName, string? Description, string? Theme, string[]? Tags, string? WelcomeMessage);

public sealed class CustomRoomService
{
    private readonly ConcurrentDictionary<Guid, RoomCustomization> _customizations = new();

    public RoomCustomization? Get(Guid roomId)
    {
        return _customizations.TryGetValue(roomId, out var customization) ? customization : null;
    }

    public RoomCustomization? Upsert(Guid roomId, UserAccount owner, CustomizeRoomRequest request)
    {
        var displayName = (request.DisplayName ?? string.Empty).Trim();
        if (displayName.Length is < 1 or > 64)
        {
            return null;
        }

        var description = (request.Description ?? string.Empty).Trim();
        var theme = string.IsNullOrWhiteSpace(request.Theme) ? "default" : request.Theme.Trim();
        var tags = (request.Tags ?? [])
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
        var welcome = string.IsNullOrWhiteSpace(request.WelcomeMessage) ? null : request.WelcomeMessage.Trim()[..Math.Min(request.WelcomeMessage.Trim().Length, 240)];
        var revision = _customizations.TryGetValue(roomId, out var previous) ? previous.Revision + 1 : 1;
        var customization = new RoomCustomization(roomId, displayName, description[..Math.Min(description.Length, 1000)], theme[..Math.Min(theme.Length, 32)], tags, welcome, revision, DateTimeOffset.UtcNow, owner.Id);
        _customizations[roomId] = customization;
        return customization;
    }

    public bool Delete(Guid roomId, UserAccount owner)
    {
        return _customizations.TryGetValue(roomId, out var current) && current.UpdatedBy == owner.Id && _customizations.TryRemove(roomId, out _);
    }

    public int Count => _customizations.Count;
}
