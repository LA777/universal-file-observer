using Microsoft.AspNetCore.Mvc.Filters;
using Ufo.Extensions;
using Ufo.Server.Services;

namespace Ufo.Server.Attributes;

/// <summary>
/// Validates that the request contains valid JWT claims and extracts user identity.
/// Stores the extracted userId in HttpContext.Items["UserId"] for access in controllers via HttpContextExtension.
/// 
/// Must be used on controllers/actions that also have [Authorize] attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class JwtClaimsRequiredAttribute : Attribute, IAsyncAuthorizationFilter
{
    // TODO LA - Add Unit Tests
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var jwtClaimsService = context.HttpContext.RequestServices.GetRequiredService<IJwtClaimsService>();
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<JwtClaimsRequiredAttribute>>();

        var userId = jwtClaimsService.GetUserId();

        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning("Unable to extract user ID from JWT token");
            context.Result = new Microsoft.AspNetCore.Mvc.UnauthorizedObjectResult("Unable to extract user identity from JWT token");
            return;
        }

        // Store userId in HttpContext.Items for access in controller actions via HttpContextExtension
        context.HttpContext.SetUserId(userId);

        await Task.CompletedTask;
    }
}
