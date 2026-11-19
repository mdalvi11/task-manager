using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.IdentityModel.Tokens;
using TaskManager.Data;
using TaskManager.Models;
using LoginRequest = TaskManager.Models.LoginRequest;
using RegisterRequest = TaskManager.Models.RegisterRequest;

namespace TaskManager.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/register", async (RegisterRequest req, IDbConnectionFactory dbFactory, IConfiguration config, HttpResponse response) =>
        {
            if (string.IsNullOrWhiteSpace(req.FullName) || string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
                return Results.BadRequest(new { error = "FullName, Email, and Password are required." });

            using var db = dbFactory.CreateConnection();

            var exists = await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM Users WHERE Email = @Email", new { req.Email });
            if (exists > 0)
                return Results.Conflict(new { error = "Email is already registered." });

            var passwordHash = BCrypt.Net.BCrypt.HashPassword(req.Password);

            const string sql = """
                INSERT INTO Users (FullName, Email, PasswordHash)
                OUTPUT INSERTED.UserId, INSERTED.FullName, INSERTED.Email, INSERTED.CreatedAt
                VALUES (@FullName, @Email, @PasswordHash);
                """;

            var user = await db.QuerySingleAsync<UserDto>(sql, new { req.FullName, req.Email, PasswordHash = passwordHash });
            SetAuthCookie(response, user, config);

            return Results.Created("/api/auth/me", new AuthResponse("Registered successfully", user));
        });

        group.MapPost("/login", async (LoginRequest req, IDbConnectionFactory dbFactory, IConfiguration config, HttpResponse response) =>
        {
            using var db = dbFactory.CreateConnection();

            const string sql = "SELECT UserId, FullName, Email, PasswordHash, CreatedAt FROM Users WHERE Email = @Email";
            var userEntity = await db.QuerySingleOrDefaultAsync<UserEntity>(sql, new { req.Email });

            if (userEntity is null || !BCrypt.Net.BCrypt.Verify(req.Password, userEntity.PasswordHash))
                return Results.Json(new { error = "Invalid email or password" }, statusCode: 401);

            var userDto = new UserDto(userEntity.UserId, userEntity.FullName, userEntity.Email, userEntity.CreatedAt);
            SetAuthCookie(response, userDto, config);

            return Results.Ok(new AuthResponse("Logged in successfully", userDto));
        });

        group.MapPost("/logout", (HttpResponse response) =>
        {
            response.Cookies.Delete("access_token", new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = false
            });
            return Results.Ok(new { message = "Logged out" });
        });

        group.MapGet("/me", async (ClaimsPrincipal principal, IDbConnectionFactory dbFactory) =>
        {
            var userId = principal.GetCurrentUserId();
            using var db = dbFactory.CreateConnection();

            const string sql = "SELECT UserId, FullName, Email, CreatedAt FROM Users WHERE UserId = @UserId";
            var user = await db.QuerySingleOrDefaultAsync<UserDto>(sql, new { UserId = userId });

            return user is null ? Results.NotFound(new { error = "User not found" }) : Results.Ok(user);
        }).RequireAuthorization();
    }

    private static void SetAuthCookie(HttpResponse response, UserDto user, IConfiguration config)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Secret"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.FullName)
        };

        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: creds
        );

        var jwtString = new JwtSecurityTokenHandler().WriteToken(token);

        response.Cookies.Append("access_token", jwtString, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = false,
            Expires = DateTimeOffset.UtcNow.AddDays(7)
        });
    }
}