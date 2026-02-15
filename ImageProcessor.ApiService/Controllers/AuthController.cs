using ImageProcessor.ApiService.Models.DTOs;
using ImageProcessor.ApiService.Services;
using Microsoft.AspNetCore.Mvc;

namespace ImageProcessor.ApiService.Controllers;

[ApiController]
[Route("[controller]")]
public class AuthController(AuthService auth) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var result = await auth.Register(request);
        return result is null ? Conflict("Email already in use") : Ok(result);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest login)
    {
        var result = await auth.Login(login);
        return result is null ? Unauthorized() : Ok(result);
    }
}