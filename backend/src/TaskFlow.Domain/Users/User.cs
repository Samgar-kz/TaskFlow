using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.Users;

public enum UserRole
{
    Member = 0,
    Admin = 1,
}

public sealed class User : Entity, IAuditable
{
    public const int NameMaxLength = 100;
    public const int JobTitleMaxLength = 100;

    private User()
    {
        // Required by EF Core.
    }

    public User(string email, string fullName, string jobTitle, UserRole role = UserRole.Member)
    {
        Email = NormalizeEmail(email);
        UpdateProfile(fullName, jobTitle);
        Role = role;
    }

    public string Email { get; private set; } = string.Empty;

    public string FullName { get; private set; } = string.Empty;

    public string JobTitle { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public UserRole Role { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public void SetPasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new DomainException("Password hash is required.");
        }

        PasswordHash = passwordHash;
    }

    public void UpdateProfile(string fullName, string jobTitle)
    {
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length > NameMaxLength)
        {
            throw new DomainException($"Full name is required and must be at most {NameMaxLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(jobTitle) || jobTitle.Trim().Length > JobTitleMaxLength)
        {
            throw new DomainException($"Job title is required and must be at most {JobTitleMaxLength} characters.");
        }

        FullName = fullName.Trim();
        JobTitle = jobTitle.Trim();
    }

    public void ChangeRole(UserRole role)
    {
        if (!Enum.IsDefined(role))
        {
            throw new DomainException("Unknown role.");
        }

        Role = role;
    }

    public void RecordLogin(DateTimeOffset at) => LastLoginAt = at;
}
