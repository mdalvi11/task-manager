using System.Data;
using System.Security.Claims;
using Dapper;

namespace TaskManager.Endpoints;

public static class EndpointHelpers
{
    public static int GetCurrentUserId(this ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? user.FindFirstValue("sub");

        if (!int.TryParse(sub, out var userId))
            throw new UnauthorizedAccessException("Invalid or missing user token.");

        return userId;
    }

    public static async Task<string?> GetUserProjectRoleAsync(IDbConnection db, int projectId, int userId)
    {
        const string sql = "SELECT Role FROM ProjectMembers WHERE ProjectId = @ProjectId AND UserId = @UserId";
        return await db.QuerySingleOrDefaultAsync<string>(sql, new { ProjectId = projectId, UserId = userId });
    }
}