using System.Security.Claims;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Domain.Users;
using TaskFlow.Infrastructure.Security;

namespace TaskFlow.Api.Infrastructure;

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid Id =>
        Guid.TryParse(Principal?.FindFirstValue(TaskFlowClaims.Subject), out var id)
            ? id
            : throw new UnauthorizedException("Authentication is required.");

    public bool IsAdmin => Principal?.IsInRole(nameof(UserRole.Admin)) == true;

    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;
}
