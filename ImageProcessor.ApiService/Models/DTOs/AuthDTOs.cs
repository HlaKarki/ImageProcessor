using System.ComponentModel.DataAnnotations;

namespace ImageProcessor.ApiService.Models.DTOs;

public record RegisterRequest(
    [Required, MinLength(2),  MaxLength(100)]
    string Name,
    
    [Required, EmailAddress,  MaxLength(256)]
    string Email,
    
    [Required, MinLength(8), MaxLength(128)]
    string Password
     // RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[\W_]).+$",
     //     ErrorMessage = "Password must contain uppercase, lowercase, digit, and special character.")]
);

public record LoginRequest(
    [Required, EmailAddress, MaxLength(256)]
    string Email,
    
    [Required, MinLength(8), MaxLength(128)]
    string Password
);

public record AuthResponse(string Token, string Email, string Name);
