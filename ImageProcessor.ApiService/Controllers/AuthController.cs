using ImageProcessor.ApiService.Models.DTOs;
using ImageProcessor.ApiService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ImageProcessor.ApiService.Controllers;

[ApiController]
[Route("/api/[controller]")]
public class AuthController(AuthService auth) : ControllerBase
{
    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var result = await auth.Register(request);
        return result is null ? Conflict("Email already in use") : Ok(result);
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginRequest login)
    {
        var result = await auth.Login(login);
        return result is null ? Unauthorized() : Ok(result);
    }
}