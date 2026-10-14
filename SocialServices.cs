using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace OpenRec_2;

public sealed record ModerationReport(Guid Id, Guid ReporterId, Guid? TargetUserId, Guid? RoomId, string Reason, DateTimeOffset CreatedAt, bool Resolved);
public sealed record BanRecord(Guid UserId, Guid ModeratorId, string Reason, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt);
public sealed record ChatMessage(Guid Id, Guid RoomId, Guid SenderId, string SenderName, string Content, DateTimeOffset CreatedAt);
public sealed record FriendRequest(Guid RequesterId, Guid RecipientId, DateTimeOffset CreatedAt);
public sealed record ReportRequest(Guid? TargetUserId, Guid? RoomId, string Reason);
public sealed record BanUserRequest(Guid UserId, string Reason, int? DurationMinutes);
public sealed record SendMessageRequest(string Content);

public sealed class ModerationService
{
    private readonly ConcurrentDictionary<Guid, ModerationReport> _reports = new();
    private readonly ConcurrentDictionary<Guid, BanRecord> _bans = new();
    private readonly HashSet<string> _moderators;
    private readonly HashSet<string> _blockedWords;

    public ModerationService(IOptions<ServerOptions> options)
    {
        _moderators = options.Value.ModeratorUsernames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _blockedWords = options.Value.BlockedChatWords
            .Where(word => !string.IsNullOrWhiteSpace(word))
            .Select(word => word.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public bool IsModerator(UserAccount user) => _moderators.Contains(user.Username);

    public ModerationReport CreateReport(UserAccount reporter, ReportRequest request)
    {
        var report = new ModerationReport(Guid.NewGuid(), reporter.Id, request.TargetUserId, request.RoomId, request.Reason.Trim()[..Math.Min(request.Reason.Trim().Length, 500)], DateTimeOffset.UtcNow, false);
        _reports[report.Id] = report;
        return report;
    }

    public BanRecord? Ban(UserAccount moderator, BanUserRequest request)
    {
        if (!IsModerator(moderator) || request.UserId == moderator.Id || string.IsNullOrWhiteSpace(request.Reason))
        {
            return null;
        }
        var expiresAt = request.DurationMinutes is > 0
            ? DateTimeOffset.UtcNow.AddMinutes(Math.Min(request.DurationMinutes.Value, 43200))
            : null;
        var ban = new BanRecord(request.UserId, moderator.Id, request.Reason.Trim()[..Math.Min(request.Reason.Trim().Length, 500)], DateTimeOffset.UtcNow, expiresAt);
        _bans[request.UserId] = ban;
        return ban;
    }

    public bool Unban(UserAccount moderator, Guid userId)
    {
        return IsModerator(moderator) && _bans.TryRemove(userId, out _);
    }

    public bool IsBanned(Guid userId)
    {
        if (!_bans.TryGetValue(userId, out var ban))
        {
            return false;
        }
        if (ban.ExpiresAt is not null && ban.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _bans.TryRemove(userId, out _);
            return false;
        }
        return true;
    }

    public IReadOnlyList<ModerationReport> Reports(bool includeResolved = false) =>
        _reports.Values.Where(report => includeResolved || !report.Resolved).OrderByDescending(report => report.CreatedAt).ToArray();

    public IReadOnlyList<BanRecord> Bans => _bans.Values.OrderByDescending(ban => ban.CreatedAt).ToArray();

    public bool ContainsBlockedWord(string content) => _blockedWords.Any(word => content.Contains(word, StringComparison.OrdinalIgnoreCase));
}

public sealed class FriendshipService
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, byte>> _friends = new();
    private readonly ConcurrentDictionary<(Guid From, Guid To), FriendRequest> _pending = new();

    public bool Send(UserAccount requester, Guid recipientId)
    {
        if (requester.Id == recipientId || AreFriends(requester.Id, recipientId))
        {
            return false;
        }
        return _pending.TryAdd((requester.Id, recipientId), new FriendRequest(requester.Id, recipientId, DateTimeOffset.UtcNow));
    }

    public bool Accept(UserAccount recipient, Guid requesterId)
    {
        if (!_pending.TryRemove((requesterId, recipient.Id), out _))
        {
            return false;
        }
        AddFriend(recipient.Id, requesterId);
        AddFriend(requesterId, recipient.Id);
        return true;
    }

    public bool Decline(UserAccount recipient, Guid requesterId) => _pending.TryRemove((requesterId, recipient.Id), out _);

    public bool Remove(UserAccount user, Guid friendId)
    {
        var removed = RemoveFriend(user.Id, friendId);
        removed |= RemoveFriend(friendId, user.Id);
        return removed;
    }

    public bool AreFriends(Guid first, Guid second) => _friends.TryGetValue(first, out var friends) && friends.ContainsKey(second);

    public IReadOnlyList<Guid> ListFriends(Guid userId) =>
        _friends.TryGetValue(userId, out var friends) ? friends.Keys.OrderBy(id => id).ToArray() : [];

    public IReadOnlyList<FriendRequest> ListPending(Guid recipientId) =>
        _pending.Values.Where(request => request.RecipientId == recipientId).OrderBy(request => request.CreatedAt).ToArray();

    private void AddFriend(Guid userId, Guid friendId) => _friends.GetOrAdd(userId, _ => new()).TryAdd(friendId, 0);

    private bool RemoveFriend(Guid userId, Guid friendId) => _friends.TryGetValue(userId, out var friends) && friends.TryRemove(friendId, out _);
}

public sealed class ChatService
{
    private readonly ConcurrentDictionary<Guid, List<ChatMessage>> _messages = new();
    private readonly object _syncRoot = new();

    public ChatMessage? Add(UserAccount sender, Guid roomId, string content, bool blocked)
    {
        var normalized = content.Trim();
        if (blocked || normalized.Length is < 1 or > 500)
        {
            return null;
        }
        var message = new ChatMessage(Guid.NewGuid(), roomId, sender.Id, sender.Username, normalized, DateTimeOffset.UtcNow);
        lock (_syncRoot)
        {
            var history = _messages.GetOrAdd(roomId, _ => []);
            history.Add(message);
            if (history.Count > 200)
            {
                history.RemoveRange(0, history.Count - 200);
            }
        }
        return message;
    }

    public IReadOnlyList<ChatMessage> History(Guid roomId, int limit)
    {
        lock (_syncRoot)
        {
            if (!_messages.TryGetValue(roomId, out var history))
            {
                return [];
            }
            return history.TakeLast(Math.Clamp(limit, 1, 200)).ToArray();
        }
    }
}
