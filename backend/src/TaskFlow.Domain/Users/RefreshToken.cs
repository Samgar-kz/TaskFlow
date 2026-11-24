using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.Users;

/// <summary>
/// A refresh token issued to a user. Only a SHA-256 hash of the token is stored,
/// so a database leak does not expose usable tokens. Tokens are single-use: every
/// refresh rotates the token and links the old one to its replacement.
/// </summary>
public sealed class RefreshToken : Entity
{
    private RefreshToken()
    {
        // Required by EF Core.
    }

    public RefreshToken(Guid userId, string tokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        if (expiresAt <= createdAt)
        {
            throw new DomainException("Refresh token must expire after it is created.");
        }

        UserId = userId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? ReplacedByTokenId { get; private set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public void Revoke(DateTimeOffset at, Guid? replacedByTokenId = null)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = at;
        ReplacedByTokenId = replacedByTokenId;
    }
}
