using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Domain.Users;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace TaskFlow.Application.Auth;

public sealed record RegisterRequest(string Email, string Password, string FullName, string JobTitle);

public sealed record LoginRequest(string Email, string Password);

public sealed record UpdateProfileRequest(string FullName, string JobTitle);

public sealed record UserDto(Guid Id, string Email, string FullName, string JobTitle, UserRole Role);

public sealed record AuthResult(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    UserDto User);

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8)
            .MaximumLength(128)
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.");
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(User.NameMaxLength);
        RuleFor(x => x.JobTitle).NotEmpty().MaximumLength(User.JobTitleMaxLength);
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(254);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(User.NameMaxLength);
        RuleFor(x => x.JobTitle).NotEmpty().MaximumLength(User.JobTitleMaxLength);
    }
}

public sealed class AuthService(
    IAppDbContext db,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    TimeProvider clock,
    IValidator<RegisterRequest> registerValidator,
    IValidator<LoginRequest> loginValidator,
    IValidator<UpdateProfileRequest> profileValidator)
{
    private const string InvalidCredentials = "Invalid email or password.";
    private const string InvalidRefreshToken = "The session has expired. Please sign in again.";

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        await registerValidator.ValidateAndThrowAsync(request, ct);

        var email = User.NormalizeEmail(request.Email);
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            throw new ConflictException("An account with this email already exists.");
        }

        var user = new User(email, request.FullName, request.JobTitle);
        user.SetPasswordHash(passwordHasher.Hash(user, request.Password));
        user.RecordLogin(clock.GetUtcNow());
        db.Users.Add(user);

        var result = IssueTokens(user);
        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        await loginValidator.ValidateAndThrowAsync(request, ct);

        var email = User.NormalizeEmail(request.Email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            // Hash anyway so response time does not reveal whether the email is registered.
            passwordHasher.Hash(new User("timing@taskflow.local", "Timing", "Timing"), request.Password);
            throw new UnauthorizedException(InvalidCredentials);
        }

        var check = passwordHasher.Verify(user, request.Password);
        if (check == PasswordCheck.Failed)
        {
            throw new UnauthorizedException(InvalidCredentials);
        }

        if (check == PasswordCheck.SuccessRehashNeeded)
        {
            user.SetPasswordHash(passwordHasher.Hash(user, request.Password));
        }

        user.RecordLogin(clock.GetUtcNow());
        var result = IssueTokens(user);
        await db.SaveChangesAsync(ct);
        return result;
    }

    /// <summary>Rotates a refresh token. Reusing an already-rotated token revokes all of the user's sessions.</summary>
    public async Task<AuthResult> RefreshAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new UnauthorizedException(InvalidRefreshToken);
        }

        var hash = tokenService.HashRefreshToken(refreshToken);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct)
            ?? throw new UnauthorizedException(InvalidRefreshToken);
        var now = clock.GetUtcNow();

        if (!stored.IsActive(now))
        {
            if (stored.RevokedAt is not null)
            {
                var active = await db.RefreshTokens
                    .Where(t => t.UserId == stored.UserId && t.RevokedAt == null && t.ExpiresAt > now)
                    .ToListAsync(ct);
                active.ForEach(t => t.Revoke(now));
                await db.SaveChangesAsync(ct);
            }

            throw new UnauthorizedException(InvalidRefreshToken);
        }

        var user = await db.Users.SingleAsync(u => u.Id == stored.UserId, ct);
        var result = IssueTokens(user, stored);
        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var hash = tokenService.HashRefreshToken(refreshToken);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is not null)
        {
            stored.Revoke(clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<UserDto> GetProfileAsync(Guid userId, CancellationToken ct) =>
        ToDto(await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, ct)
              ?? throw new NotFoundException("User", userId));

    public async Task<UserDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct)
    {
        await profileValidator.ValidateAndThrowAsync(request, ct);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("User", userId);
        user.UpdateProfile(request.FullName, request.JobTitle);
        await db.SaveChangesAsync(ct);
        return ToDto(user);
    }

    internal static UserDto ToDto(User user) => new(user.Id, user.Email, user.FullName, user.JobTitle, user.Role);

    private AuthResult IssueTokens(User user, RefreshToken? replacing = null)
    {
        var now = clock.GetUtcNow();
        var accessToken = tokenService.CreateAccessToken(user);
        var issued = tokenService.CreateRefreshToken();

        var refreshToken = new RefreshToken(user.Id, issued.Hash, now, issued.ExpiresAt);
        db.RefreshTokens.Add(refreshToken);
        replacing?.Revoke(now, refreshToken.Id);

        return new AuthResult(accessToken.Token, accessToken.ExpiresAt, issued.Token, issued.ExpiresAt, ToDto(user));
    }
}
