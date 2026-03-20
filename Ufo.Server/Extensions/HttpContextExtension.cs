namespace Ufo.Extensions;

/// <summary>
/// Extension methods for HttpContext to work with authentication data.
/// </summary>
public static class HttpContextExtension
{
    // TODO LA - Add Unit Tests
    private const string UserIdKey = "UserId";

    /// <summary>
    /// Gets the user ID from HttpContext items and returns it as a ULID.
    /// Throws UnauthorizedAccessException if user ID is not found or invalid.
    /// </summary>
    /// <param name="httpContext">The HttpContext instance.</param>
    /// <returns>The user ID as a ULID.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when user ID is not found or invalid.</exception>
    public static Ulid GetUserIdAsUlid(this HttpContext httpContext)
    {
        if (httpContext?.Items == null)
        {
            throw new UnauthorizedAccessException("HttpContext or items collection is null.");
        }

        if (httpContext.Items.TryGetValue(UserIdKey, out var userIdObj))
        {
            if (userIdObj is Ulid ulid && ulid != Ulid.Empty)
            {
                return ulid;
            }

            if (userIdObj is string userIdString && !string.IsNullOrWhiteSpace(userIdString))
            {
                if (Ulid.TryParse(userIdString, out var parsedUlid))
                {
                    return parsedUlid;
                }
            }
        }

        throw new UnauthorizedAccessException("User ID not found in HttpContext.");
    }


    /// <summary>
    /// Gets the user ID from HttpContext items as a string.
    /// </summary>
    /// <param name="httpContext">The HttpContext instance.</param>
    /// <returns>The user ID as a string, or null if not found.</returns>
    public static string? GetUserId(this HttpContext httpContext)
    {
        if (httpContext?.Items == null)
        {
            return null;
        }

        if (httpContext.Items.TryGetValue(UserIdKey, out var userIdObj))
        {
            return userIdObj switch
            {
                string str => str,
                Ulid ulid => ulid.ToString(),
                _ => userIdObj?.ToString()
            };
        }

        return null;
    }

    /// <summary>
    /// Sets the user ID in HttpContext items.
    /// </summary>
    /// <param name="httpContext">The HttpContext instance.</param>
    /// <param name="userId">The user ID to set.</param>
    public static void SetUserId(this HttpContext httpContext, string userId)
    {
        if (httpContext?.Items != null)
        {
            httpContext.Items[UserIdKey] = userId;
        }
    }

    /// <summary>
    /// Sets the user ID in HttpContext items from a ULID.
    /// </summary>
    /// <param name="httpContext">The HttpContext instance.</param>
    /// <param name="userId">The user ID as a ULID.</param>
    public static void SetUserId(this HttpContext httpContext, Ulid userId)
    {
        if (httpContext?.Items != null)
        {
            httpContext.Items[UserIdKey] = userId.ToString();
        }
    }

    /// <summary>
    /// Checks if a user ID exists in HttpContext items.
    /// </summary>
    /// <param name="httpContext">The HttpContext instance.</param>
    /// <returns>True if user ID exists and is not empty, false otherwise.</returns>
    public static bool HasUserId(this HttpContext httpContext)
    {
        try
        {
            var userId = httpContext.GetUserIdAsUlid();
            return userId != Ulid.Empty;
        }
        catch
        {
            return false;
        }
    }
}
