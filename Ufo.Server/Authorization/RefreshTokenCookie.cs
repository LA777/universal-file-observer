namespace Ufo.Server.Authorization;

/// <summary>
/// The cookie the refresh token travels in, and the flags that make choosing a
/// cookie over a JSON field worthwhile.
/// </summary>
/// <remarks>
/// <para>
/// The access token is handed to the SPA in the response body and kept in
/// localStorage, where any script on the page can read it. That is survivable for
/// a token measured in minutes; it would not be for one measured in weeks. So the
/// refresh token never reaches JavaScript at all: <c>HttpOnly</c> keeps it out of
/// reach of an injected script, and the browser attaches it to the one endpoint
/// that needs it without the application ever holding it.
/// </para>
/// <para>
/// <c>SameSite=Strict</c> is affordable here because the SPA is served from the
/// same origin as the API, so no legitimate cross-site request ever needs this
/// cookie. That is also what stands in for CSRF protection on the refresh
/// endpoint: another site cannot cause the browser to send it.
/// </para>
/// <para>
/// <c>Path</c> narrows it to the auth endpoints, so it is not attached to every
/// snapshot or file request that has no use for it.
/// </para>
/// </remarks>
public static class RefreshTokenCookie
{
    public const string Name = "ufo_refresh_token";

    private const string Path = "/api/auth";

    public static void Write(HttpResponse response, string refreshToken, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Cookies.Append(Name, refreshToken, BuildOptions(options => options.MaxAge = lifetime));
    }

    /// <summary>
    /// Removes the cookie. The flags have to match the ones it was written with,
    /// or the browser keeps the original alongside the deletion.
    /// </summary>
    public static void Clear(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Cookies.Delete(Name, BuildOptions());
    }

    public static string? Read(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Cookies[Name];
    }

    private static CookieOptions BuildOptions(Action<CookieOptions>? configure = null)
    {
        var options = new CookieOptions
        {
            HttpOnly = true,

            // Always, including the deployments that serve plaintext behind a
            // reverse proxy: there the browser still speaks HTTPS to the proxy,
            // which is the leg this flag protects. Browsers exempt localhost, so
            // development over http keeps working.
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = Path
        };

        configure?.Invoke(options);

        return options;
    }
}
