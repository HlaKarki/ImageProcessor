namespace ImageProcessor.ApiService.Middleware;

public class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext httpContext)
    {
        httpContext.Response.Headers["X-Content-Type-Options"] = "nosniff";
        httpContext.Response.Headers["X-Frame-Options"] = "DENY";
        httpContext.Response.Headers["X-XSS-Protection"] = "0"; // disabled in favour of CSP
        httpContext.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        httpContext.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        httpContext.Response.Headers["Content-Security-Policy"] =
            "default-src 'none'; frame-ancestors 'none'";
        
        await next(httpContext);
    }
}