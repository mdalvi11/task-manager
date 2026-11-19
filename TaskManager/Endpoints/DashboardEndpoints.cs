using System.Security.Claims;
using Dapper;
using TaskManager.Data;
using TaskManager.Models;

namespace TaskManager.Endpoints;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboard").RequireAuthorization();

        // GET /api/dashboard/overview
        group.MapGet("/overview", async (ClaimsPrincipal principal, IDbConnectionFactory dbFactory) =>
        {
            var userId = principal.GetCurrentUserId();
            using var db = dbFactory.CreateConnection();

            const string projectsSql = """
                                       SELECT v.*
                                       FROM vw_ProjectSummary v
                                       INNER JOIN ProjectMembers pm ON v.ProjectId = pm.ProjectId
                                       WHERE pm.UserId = @UserId;
                                       """;

            var projects = (await db.QueryAsync<ProjectSummaryDto>(projectsSql, new { UserId = userId })).ToList();
            var workload = (await db.QueryAsync<UserWorkloadDto>("SELECT * FROM vw_UserWorkload ORDER BY ActiveTasks DESC")).ToList();

            var overview = new DashboardOverviewDto(
                TotalProjects: projects.Count,
                ActiveProjects: projects.Count(p => p.Status == "Active"),
                TotalTasks: projects.Sum(p => p.TotalTasks),
                CompletedTasks: projects.Sum(p => p.CompletedTasks),
                OverdueTasks: projects.Sum(p => p.OverdueTasks),
                Projects: projects,
                TeamWorkload: workload
            );

            return Results.Ok(overview);
        });

        // GET /api/dashboard/workload
        group.MapGet("/workload", async (IDbConnectionFactory dbFactory) =>
        {
            using var db = dbFactory.CreateConnection();
            var workload = await db.QueryAsync<UserWorkloadDto>("SELECT * FROM vw_UserWorkload ORDER BY ActiveTasks DESC");
            return Results.Ok(workload);
        });
    }
}