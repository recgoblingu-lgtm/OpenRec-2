using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace OpenRec_2;

public sealed record UserAccount(Guid Id, string Username, DateTimeOffset CreatedAt);

public sealed record AuthResult(UserAccount User, string Token, DateTimeOffset ExpiresAt);

public sealed class AuthService
{
    private readonly ConcurrentDictionary<string, StoredAccount> _accounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private readonly TimeSpan _sessionLifetime = TimeSpan.FromDays(7);

    public UserAccount? Register(string username, string password)
    {
        var normalized = username.Trim();
        if (normalized.Length is < 3 or > 32 || password.Length is < 8 or > 256)
        {
            return null;
        }

        var account = new UserAccount(Guid.NewGuid(), normalized, DateTimeOffset.UtcNow);
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Hash(password, salt);
        return _accounts.TryAdd(normalized, new StoredAccount(account, salt, hash)) ? account : null;
    }

    public AuthResult? Login(string username, string password)
    {
        if (!_accounts.TryGetValue(username.Trim(), out var account))
        {
            return null;
        }

        var candidate = Hash(password, account.Salt);
        if (!CryptographicOperations.FixedTimeEquals(candidate, account.PasswordHash))
        {
            return null;
        }

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');
        var expiresAt = DateTimeOffset.UtcNow.Add(_sessionLifetime);
        _sessions[token] = new Session(account.User, expiresAt);
        return new AuthResult(account.User, token, expiresAt);
    }

    public UserAccount? Authenticate(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = header[7..].Trim();
        if (!_sessions.TryGetValue(token, out var session))
        {
            return null;
        }

        if (session.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _sessions.TryRemove(token, out _);
            return null;
        }

        return session.User;
    }

    public bool Logout(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return _sessions.TryRemove(header[7..].Trim(), out _);
    }

    public int UserCount => _accounts.Count;
    public int SessionCount => _sessions.Count;

    private static byte[] Hash(string password, byte[] salt)
    {
        using var derive = new Rfc2898DeriveBytes(password, salt, 120000, HashAlgorithmName.SHA256);
        return derive.GetBytes(32);
    }

    private sealed record StoredAccount(UserAccount User, byte[] Salt, byte[] PasswordHash);
    private sealed record Session(UserAccount User, DateTimeOffset ExpiresAt);
}

public sealed record RegisterRequest(string Username, string Password);
public sealed record LoginRequest(string Username, string Password);
