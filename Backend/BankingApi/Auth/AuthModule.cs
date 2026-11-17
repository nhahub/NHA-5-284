using BankingApi.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BankingApi.Auth;

public static class AuthModule
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/auth");

        group.MapPost("/register", async (RegisterRequest request, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) ||
                string.IsNullOrWhiteSpace(request.Password) ||
                request.Password.Length < 8)
            {
                return Results.BadRequest(new { message = "Invalid email or password. Password must be at least 8 characters long." });
            }

            var emailNormalized = request.Email.Trim().ToLowerInvariant();

            var exists = await db.Users.AnyAsync(u => u.Email == emailNormalized);
            if (exists)
            {
                return Results.Conflict(new { message = "A user with this email already exists." });
            }

            var user = new User
            {
                Email = emailNormalized,
                PasswordHash = PasswordHasher.HashPassword(request.Password),
                Role = "Customer",
                CreatedAt = DateTime.UtcNow
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();

            return Results.Created($"/auth/{user.Id}", new { userId = user.Id });
        });

        group.MapPost("/login", async (LoginRequest request, AppDbContext db, TokenService tokenService) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.Json(new { message = "Invalid email or password." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var emailNormalized = request.Email.Trim().ToLowerInvariant();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == emailNormalized);

            if (user is null || !PasswordHasher.VerifyPassword(request.Password, user.PasswordHash))
            {
                return Results.Json(new { message = "Invalid email or password." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var (token, expiresAt) = tokenService.GenerateJwt(user);
            return Results.Ok(new { token, expiresAt });
        });

        group.MapGet("/me", (System.Security.Claims.ClaimsPrincipal user) =>
        {
            var userId = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? user.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
            var email = user.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                ?? user.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email)?.Value;
            var role = user.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value
                ?? user.FindFirst("role")?.Value;
            return Results.Ok(new { userId, email, role });
        }).RequireAuthorization();

        return endpoints;
    }

    public record RegisterRequest(string? Email, string? Password);
    public record LoginRequest(string? Email, string? Password);
}
