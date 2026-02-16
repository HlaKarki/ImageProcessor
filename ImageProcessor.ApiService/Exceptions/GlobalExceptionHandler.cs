using Microsoft.AspNetCore.Diagnostics;

namespace ImageProcessor.ApiService.Exceptions;

public class GlobalExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            UnauthorizedAccessException => (401, "Unauthorized"),
            KeyNotFoundException => (404, "Not found"),
            ArgumentException => (400, exception.Message),
            _ => (500, "An unexpected error occured")
        };

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(new { status, title }, cancellationToken);

        return true;
    }
}