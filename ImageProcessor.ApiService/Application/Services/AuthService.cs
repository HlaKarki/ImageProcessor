using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ImageProcessor.ApiService.Application.DTOs;
using ImageProcessor.ApiService.Domain.Entities;
using ImageProcessor.ApiService.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace ImageProcessor.ApiService.Application.Services;

public class AuthService(AppDbContext db, IConfiguration configuration)
{
    public async Task<AuthResponse?> Register(RegisterRequest request)
    {
        if (await db.Users.AnyAsync(user => user.Email == request.Email)) return null;

        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            CreatedAt = DateTime.UtcNow
        };
        
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return new AuthResponse(GenerateToken(user), user.Email, user.Name);
    }

    public async Task<AuthResponse?> Login(LoginRequest request)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == request.Email);

        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash)) return null;

        user.LastLogin = DateTime.UtcNow;
        await db.SaveChangesAsync();
        
        return new AuthResponse(GenerateToken(user), user.Email, user.Name);
    }

    private string GenerateToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Secret"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Name, user.Name)
        };

        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"],
            audience: configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(int.Parse(configuration["Jwt:ExpiryMinutes"]!)),
            signingCredentials: credentials
        );
        
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
