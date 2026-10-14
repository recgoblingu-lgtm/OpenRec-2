using System.Collections.Concurrent;

namespace OpenRec_2;

public sealed record RoomSummary(
    Guid Id,
    string Name,
    string Activity,
    string Region,
    Guid OwnerId,
    int MemberCount,
    int Capacity,
    bool IsPrivate,
    DateTimeOffset CreatedAt);

public sealed class RoomService
{
    private readonly ConcurrentDictionary<Guid, Room> _rooms = new();

    public RoomSummary? Create(UserAccount owner, string name, string activity, string region, int capacity, bool isPrivate)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 64 || capacity is < 1 or > 100)
        {
            return null;
        }

        var room = new Room
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Activity = string.IsNullOrWhiteSpace(activity) ? "Hangout" : activity.Trim()[..Math.Min(activity.Trim().Length, 64)],
            Region = string.IsNullOrWhiteSpace(region) ? "us" : region.Trim()[..Math.Min(region.Trim().Length, 16)],
            OwnerId = owner.Id,
            Capacity = capacity,
            IsPrivate = isPrivate,
            CreatedAt = DateTimeOffset.UtcNow
        };
        room.Members.Add(owner.Id);
        _rooms[room.Id] = room;
        return ToSummary(room);
    }

    public IReadOnlyList<RoomSummary> List(string? region, string? activity)
    {
        return _rooms.Values
            .Where(room => string.IsNullOrWhiteSpace(region) || room.Region.Equals(region, StringComparison.OrdinalIgnoreCase))
            .Where(room => string.IsNullOrWhiteSpace(activity) || room.Activity.Equals(activity, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(room => room.CreatedAt)
            .Select(ToSummary)
            .ToArray();
    }

    public RoomSummary? Get(Guid id)
    {
        return _rooms.TryGetValue(id, out var room) ? ToSummary(room) : null;
    }

    public bool IsMember(Guid id, Guid userId)
    {
        return _rooms.TryGetValue(id, out var room) && lockMemberCheck(room, userId);
    }

    public bool Join(Guid id, UserAccount user)
    {
        if (!_rooms.TryGetValue(id, out var room))
        {
            return false;
        }

        lock (room.SyncRoot)
        {
            return room.Members.Contains(user.Id) || room.Members.Count < room.Capacity && room.Members.Add(user.Id);
        }
    }

    public bool Leave(Guid id, UserAccount user)
    {
        if (!_rooms.TryGetValue(id, out var room))
        {
            return false;
        }

        lock (room.SyncRoot)
        {
            if (room.OwnerId == user.Id)
            {
                return false;
            }
            return room.Members.Remove(user.Id);
        }
    }

    public bool Delete(Guid id, UserAccount user)
    {
        if (!_rooms.TryGetValue(id, out var room) || room.OwnerId != user.Id)
        {
            return false;
        }
        return _rooms.TryRemove(id, out _);
    }

    public int Count => _rooms.Count;

    private static bool lockMemberCheck(Room room, Guid userId)
    {
        lock (room.SyncRoot)
        {
            return room.Members.Contains(userId);
        }
    }

    private static RoomSummary ToSummary(Room room)
    {
        lock (room.SyncRoot)
        {
            return new RoomSummary(room.Id, room.Name, room.Activity, room.Region, room.OwnerId, room.Members.Count, room.Capacity, room.IsPrivate, room.CreatedAt);
        }
    }

    private sealed class Room
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Activity { get; init; } = string.Empty;
        public string Region { get; init; } = string.Empty;
        public Guid OwnerId { get; init; }
        public int Capacity { get; init; }
        public bool IsPrivate { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public HashSet<Guid> Members { get; } = [];
        public object SyncRoot { get; } = new();
    }
}

public sealed record CreateRoomRequest(string Name, string? Activity, string? Region, int Capacity = 8, bool IsPrivate = false);
