using System.Data;
using System.Security.Claims;
using Dapper;
using TaskManager.Data;
using TaskManager.Models;

namespace TaskManager.Endpoints;

public static class ProjectEndpoints
{
    public static void MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/projects").RequireAuthorization();

        // GET /api/projects
        group.MapGet("/", async (ClaimsPrincipal principal, IDbConnectionFactory dbFactory) =>
        {
            var userId = principal.GetCurrentUserId();
            using var db = dbFactory.CreateConnection();

            const string sql = """
                SELECT v.*
                FROM vw_ProjectSummary v
                INNER JOIN ProjectMembers pm ON v.ProjectId = pm.ProjectId
                WHERE pm.UserId = @UserId
                ORDER BY v.Deadline ASC;
                """;

            var projects = await db.QueryAsync<ProjectSummaryDto>(sql, new { UserId = userId });
            return Results.Ok(projects);
        });

        // POST /api/projects (Calls Stored Procedure sp_CreateProjectWithOwner)
        group.MapPost("/", async (CreateProjectRequest req, ClaimsPrincipal principal, IDbConnectionFactory dbFactory) =>
        {
            var userId = principal.GetCurrentUserId();
            using var db = dbFactory.CreateConnection();

            var newProjectId = await db.ExecuteScalarAsync<int>(
                "sp_CreateProjectWithOwner",
                new { req.ProjectName, req.Description, OwnerId = userId, req.Deadline },
                commandType: CommandType.StoredProcedure
            );

            var created = await db.QuerySingleAsync<ProjectSummaryDto>(
                "SELECT * FROM vw_ProjectSummary WHERE ProjectId = @ProjectId",
                new { ProjectId = newProjectId }
            );

            return Results.Created($"/api/projects/{newProjectId}", created);
        });

        // GET /api/projects/{projectId}
        group.MapGet("/{projectId:int}", async (int projectId, ClaimsPrincipal principal, IDbConnectionFactory dbFactory) =>
        {
            var userId = principal.GetCurrentUserId();
            using var db = dbFactory.CreateConnection();

            var role = await EndpointHelpers.GetUserProjectRoleAsync(db, projectId, userId);
            if (role is null)
                return Results.Forbid();

            var summary = await db.QuerySingleOrDefaultAsync<ProjectSummaryDto>(
                "SELECT * FROM vw_ProjectSummary WHERE ProjectId = @ProjectId",
                new { ProjectId = projectId }
            );

            if (summary is null)
                return Results.NotFound(new { error = "Project not found" });

            const string membersSql = """
                SELECT u.UserId, u.FullName, u.Email, pm.Role, pm.JoinedAt
                FROM ProjectMembers pm
                INNER JOIN Users u ON pm.UserId = u.UserId
                WHERE pm.ProjectId = @ProjectId
                ORDER BY pm.JoinedAt ASC;
                """;

            var members = (await db.QueryAsync<ProjectMemberDto>(membersSql, new { ProjectId = projectId })).ToList();

            var detail = new ProjectDetailDto(
                summary.ProjectId, summary.ProjectName, summary.Description,
                summary.OwnerId, summary.OwnerName, summary.Deadline, summary.Status,
                summary.TotalTasks, summary.CompletedTasks, summary.OverdueTasks, members
            );

            return Results.Ok(detail);
        });

        // PUT /api/projects/{projectId}
        group.MapPut("/{projectId:int}", async (int projectId, UpdateProjectRequest req, ClaimsPrincipal principal, IDbConnectionFactory dbFactory) =>
        {
            var userId = principal.GetCurrentUserId();
            using var db = dbFactory.CreateConnection();

            var role = await EndpointHelpers.GetUserProjectRoleAsync(db, projectId, userId);
            if (role is not ("Admin" or "Project Manager"))
                return Results.Forbid();

            const string sql = """
                UPDATE Projects
                SET ProjectName = @ProjectName, Description = @Description, Deadline = @Deadline, Status = @Status
                WHERE ProjectId = @ProjectId;
                """;

            var rows = await db.ExecuteAsync(sql, new { req.ProjectName, req.Description, req.Deadline, req.Status, ProjectId = projectId });
            if (rows == 0)
                return Results.NotFound(new { error = "Project not found" });

            var updated = await db.QuerySingleAsync<ProjectSummaryDto>(
                "SELECT * FROM vw_ProjectSummary WHERE ProjectId = @ProjectId",
                new { ProjectId = projectId }
            );

            return Results.Ok(updated);
        });

        // DELETE /api/projects/{projectId}
        group.MapDelete("/{projectId:int}", async (int projectId, ClaimsPrincipal principal, IDbConnectionFactory dbFactory) =>
        {
            var userId = principal.GetCurrentUserId();
            using var db = dbFactory.CreateConnection();

            var role = await EndpointHelpers.GetUserProjectRoleAsync(db, projectId, userId);
            if (role != "Admin")
                return Results.Forbid();

            var rows = await db.ExecuteAsync("DELETE FROM Projects WHERE ProjectId = @ProjectId", new { ProjectId = projectId });
            return rows == 0 ? Results.NotFound(new { error = "Project not found" }) : Results.NoContent();
        });

        // POST /api/projects/{projectId}/members
        group.MapPost("/{projectId:int}/members", async (int projectId, AddProjectMemberRequest req, ClaimsPrincipal principal, IDbConnectionFactory dbFactory) =>
        {
            var userId = principal.GetCurrentUserId();
            using var db = dbFactory.CreateConnection();

            var role = await EndpointHelpers.GetUserProjectRoleAsync(db, projectId, userId);
            if (role is not ("Admin" or "Project Manager"))
                return Results.Forbid();

            var targetUser = await db.QuerySingleOrDefaultAsync<UserDto>(
                "SELECT UserId, FullName, Email, CreatedAt FROM Users WHERE Email = @Email",
                new { req.Email }
            );

            if (targetUser is null)
                return Results.NotFound(new { error = "No user found with that email." });

            var alreadyMember = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM ProjectMembers WHERE ProjectId = @ProjectId AND UserId = @UserId",
                new { ProjectId = projectId, targetUser.UserId }
            );

            if (alreadyMember > 0)
                return Results.Conflict(new { error = "User is already a member of this project." });

            const string insertSql = """
                INSERT INTO ProjectMembers (ProjectId, UserId, Role)
                OUTPUT INSERTED.JoinedAt
                VALUES (@ProjectId, @UserId, @Role);
                """;

            var joinedAt = await db.ExecuteScalarAsync<DateTime>(insertSql, new { ProjectId = projectId, targetUser.UserId, req.Role });

            return Results.Created(
                $"/api/projects/{projectId}/members/{targetUser.UserId}",
                new ProjectMemberDto(targetUser.UserId, targetUser.FullName, targetUser.Email, req.Role, joinedAt)
            );
        });

        // PATCH /api/projects/{projectId}/members/{memberId}
        group.MapPatch("/{projectId:int}/members/{memberId:int}", async (int projectId, int memberId, UpdateMemberRoleRequest req, ClaimsPrincipal principal, IDbConnectionFactory dbFactory) =>
        {
            var userId = principal.GetCurrentUserId();
            using var db = dbFactory.CreateConnection();

            var role = await EndpointHelpers.GetUserProjectRoleAsync(db, projectId, userId);
            if (role != "Admin")
                return Results.Forbid();

            var rows = await db.ExecuteAsync(
                "UPDATE ProjectMembers SET Role = @Role WHERE ProjectId = @ProjectId AND UserId = @MemberId",
                new { req.Role, ProjectId = projectId, MemberId = memberId }
            );

            if (rows == 0)
                return Results.NotFound(new { error = "Member not found in project." });

            const string fetchSql = """
                SELECT u.UserId, u.FullName, u.Email, pm.Role, pm.JoinedAt
                FROM ProjectMembers pm
                INNER JOIN Users u ON pm.UserId = u.UserId
                WHERE pm.ProjectId = @ProjectId AND pm.UserId = @MemberId;
                """;

            var updated = await db.QuerySingleAsync<ProjectMemberDto>(fetchSql, new { ProjectId = projectId, MemberId = memberId });
            return Results.Ok(updated);
        });

        // DELETE /api/projects/{projectId}/members/{memberId}
        group.MapDelete("/{projectId:int}/members/{memberId:int}", async (int projectId, int memberId, ClaimsPrincipal principal, IDbConnectionFactory dbFactory) =>
        {
            var userId = principal.GetCurrentUserId();
            using var db = dbFactory.CreateConnection();

            var role = await EndpointHelpers.GetUserProjectRoleAsync(db, projectId, userId);
            if (role != "Admin")
                return Results.Forbid();

            var ownerId = await db.ExecuteScalarAsync<int>("SELECT OwnerId FROM Projects WHERE ProjectId = @ProjectId", new { ProjectId = projectId });
            if (ownerId == memberId)
                return Results.BadRequest(new { error = "Cannot remove the project owner." });

            var rows = await db.ExecuteAsync(
                "DELETE FROM ProjectMembers WHERE ProjectId = @ProjectId AND UserId = @MemberId",
                new { ProjectId = projectId, MemberId = memberId }
            );

            return rows == 0 ? Results.NotFound(new { error = "Member not found." }) : Results.NoContent();
        });
    }
}