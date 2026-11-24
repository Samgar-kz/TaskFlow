using TaskFlow.Api.Infrastructure;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace TaskFlow.Api.Controllers;

public sealed record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt, UserDto User);

/// <summary>
/// Registration, sign-in and session management. Access tokens are returned in the body and
/// kept in memory by the SPA; refresh tokens are set as an HttpOnly, SameSite=Strict cookie so
/// they are never readable from JavaScript.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(AuthService auth, ICurrentUser currentUser) : ControllerBase
{
    public const string RefreshCookieName = "taskflow_refresh";

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<AuthResponse> Register(RegisterRequest request, CancellationToken ct) =>
        WithCookie(await auth.RegisterAsync(request, ct));

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<AuthResponse> Login(LoginRequest request, CancellationToken ct) =>
        WithCookie(await auth.LoginAsync(request, ct));

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<AuthResponse> Refresh(CancellationToken ct)
    {
        try
        {
            return WithCookie(await auth.RefreshAsync(Request.Cookies[RefreshCookieName], ct));
        }
        catch (Application.Common.UnauthorizedException)
        {
            DeleteCookie();
            throw;
        }
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await auth.LogoutAsync(Request.Cookies[RefreshCookieName], ct);
        DeleteCookie();
        return NoContent();
    }

    [HttpGet("me")]
    public Task<UserDto> Me(CancellationToken ct) => auth.GetProfileAsync(currentUser.Id, ct);

    [HttpPut("me")]
    public Task<UserDto> UpdateMe(UpdateProfileRequest request, CancellationToken ct) =>
        auth.UpdateProfileAsync(currentUser.Id, request, ct);

    private AuthResponse WithCookie(AuthResult result)
    {
        Response.Cookies.Append(RefreshCookieName, result.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth",
            Expires = result.RefreshTokenExpiresAt,
            IsEssential = true,
        });

        return new AuthResponse(result.AccessToken, result.AccessTokenExpiresAt, result.User);
    }

    private void DeleteCookie() =>
        Response.Cookies.Delete(RefreshCookieName, new CookieOptions { Path = "/api/auth" });
}
