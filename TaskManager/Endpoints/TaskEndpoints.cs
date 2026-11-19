using System.Data;
using System.Security.Claims;
using Dapper;
using Microsoft.Data.SqlClient;
using TaskManager.Data;
using TaskManager.Models;

namespace TaskManager.Endpoints;

public static class TaskEndpoints
{
    public static void MapTaskEndpoints(this IEndpointRouteBuilder app)
    {
        // Project-scoped task routes
        var projectTasks = app.MapGroup("/api/projects/{projectId:int}/tasks").RequireAuthorization();

        // GET /api/projects/{projectId}/tasks?status=...&priority=...&assigneeId=...&tagId=...
        projectTasks.MapGet("/", async (
            int projectId,
            string? status,
            string? priority,
            int? assigneeId,
            int? tagId,
            ClaimsPrincipal principal,
            IDbConnectionFactory dbFactory) =>
        {
            var userId = principal.GetCurrentUserId();
            using var db = dbFactory.CreateConnection();

            if (await EndpointHelpers.GetUserProjectRoleAsync(db, projectId, userId) is null)
                return Results.Forbid();

            var sql = """
                SELECT DISTINCT v.*
                FROM vw_ProjectTaskDetails v
                LEFT JOIN TaskTags tt ON v.TaskId = tt.TaskId
                WHERE v.ProjectId = @ProjectId
                  AND (@Status IS NULL OR v.Status = @Status)
                  AND (@Priority IS NULL OR v.Priority = @Priority)
                  AND (@AssigneeId IS NULL OR v.AssigneeId = @AssigneeId)
                  AND (@TagId IS NULL OR tt.TagId = @TagId)
                ORDER BY v.CreatedAt DESC;
                """;

            var rawTasks = (await db.QueryAsync<TaskListItemRow>(sql, new
            {
                ProjectId = projectId,
                Status = string.IsNullOrWhiteSpace(status) ? null : status,
                Priority = string.IsNullOrWhiteSpace(priority) ? null : priority,
                AssigneeId = assigneeId,
                TagId = tagId
            })).ToList();

            var result = await AttachTagsToTasksAsync(db, rawTasks);
            return Results.Ok(result);
        });

        // POST /api/projects/{projectId}/tasks (Calls Stored Procedure sp_CreateTaskWithTags)
        projectTasks.MapPost("/", async (int projectId, CreateTaskRequest req, ClaimsPrincipal principal, IDbConnectionFactory dbFactory) =>
        {
            var userId = principal.GetCurrentUserId();
            using var db = dbFactory.CreateConnection();

            if (await EndpointHelpers.GetUserProjectRoleAsync(db, projectId, userId) is null)
                return Results.Forbid();

            var tagCsv = req.TagIds is { Count: > 0 } ? string.Join(",", req.TagIds) : null;

            try
            {
                var newTaskId = await db.ExecuteScalarAsync<int>(
                    "sp_CreateTaskWithTags",
                    new
                    {
                        ProjectId = projectId,
                        req.AssigneeId,
                        req.ParentTaskId,
                        req.Title,
                        req.Description,
                        req.Priority,
                        req.DueDate,
                        TagIdsCsv = tagCsv
                    },
                    commandType: CommandType.StoredProcedure
                );

                var created = await BuildTaskDetailAsync(db, newTaskId);
                return Results.Created($"/api/tasks/{newTaskId}", created);
            }
            catch (SqlException ex) when (ex.Number == 50001)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // Task-ID scoped routes
        var tasks = app.MapGroup("/api/tasks").RequireAuthorization();

        // GET /api/tasks/{taskId}
        tasks.MapGet("/{taskId:int}", async (int taskId, ClaimsPrincipal principal, IDbConnectionFactory dbFactory) =>
        {
            var userId = principal.GetCurrentUserId();
            using var db = dbFactory.CreateConnection();

            var projectId = await db.QuerySingleOrDefaultAsync<int?>("SELECT ProjectId FROM Tasks WHERE TaskId = @TaskId", new { TaskId = taskId });
            if (projectId is null)
                return Results.NotFound(new { error = "Task not found" });

            if (await EndpointHelpers.GetUserProjectRoleAsync(db, projectId.Value, userId) is null)
                return Results.Forbid();

            var detail = await BuildTaskDetailAsync(db, taskId);
            return Results.Ok(detail);
        });

        // PUT /api/tasks/{taskId}
        tasks.MapPut("/{taskId:int}", async (int taskId, UpdateTaskRequest req, ClaimsPrincipal principal, IDbConnectionFactory dbFactory) =>
        {
            var userId = principal.GetCurrentUserId();
            using var db = dbFactory.CreateConnection();

            var projectId = await db.QuerySingleOrDefaultAsync<int?>("SELECT ProjectId FROM Tasks WHERE TaskId = @TaskId", new { TaskId = taskId });
            if (projectId is null)
                return Results.NotFound(new { error = "Task not found" });

            if (await EndpointHelpers.GetUserProjectRoleAsync(db, projectId.Value, userId) is null)
                return Results.Forbid();

            const string sql = """
                UPDATE Tasks
                SET AssigneeId = @AssigneeId,
                    ParentTaskId = @ParentTaskId,
                    Title = @Title,
                    Description = @Description,
                    Priority = @Priority,
                    Status = @Status,
                    DueDate = @DueDate
                WHERE TaskId = @TaskId;
                """;

            await db.ExecuteAsync(sql, new
            {
                req.AssigneeId,
                req.ParentTaskId,
                req.Title,
                req.Description,
                req.Priority,
                req.Status,
                req.DueDate,
                TaskId = taskId
            });

            var detail = await BuildTaskDetailAsync(db, taskId);
            return Results.Ok(detail);
        });

        // PATCH /api/tasks/{taskId}/status (Fires trg_Tasks_AuditUpdate)
        tasks.MapPatch("/{taskId:int}/status", async (int taskId, UpdateTaskStatusRequest req, ClaimsPrincipal principal, IDbConnectionFactory dbFactory) =>
        {
            var userId = principal.GetCurrentUserId();
            using var db = dbFactory.CreateConnection();

            var projectId = await db.QuerySingleOrDefaultAsync<int?>("SELECT ProjectId FROM Tasks WHERE TaskId = @TaskId", new { TaskId = taskId });
            if (projectId is null)
                return Results.NotFound(new { error = "Task not found" });

            if (await EndpointHelpers.GetUserProjectRoleAsync(db, projectId.Value, userId) is null)
                return Results.Forbid();

            await db.ExecuteAsync("UPDATE Tasks SET Status = @Status WHERE TaskId = @TaskId", new { req.Status, TaskId = taskId });

            var latestAudit = await db.QuerySingleOrDefaultAsync<TaskAuditLogEntity>(
                "SELECT TOP 1 * FROM TaskAuditLogs WHERE TaskId = @TaskId ORDER BY LogId DESC",
                new { TaskId = taskId }
            );

            return Results.Ok(new { taskId, status = req.Status, latestAudit });
        });

        // PATCH /api/tasks/{taskId}/assignee (Fires trg_Tasks_AuditUpdate)
        tasks.MapPatch("/{taskId:int}/assignee", async (int taskId, UpdateTaskAssigneeRequest req, ClaimsPrincipal principal, IDbConnectionFactory dbFactory) =>
        {
            var userId = principal.GetCurrentUserId();
            using var db = dbFactory.CreateConnection();

            var projectId = await db.QuerySingleOrDefaultAsync<int?>("SELECT ProjectId FROM Tasks WHERE TaskId = @TaskId", new { TaskId = taskId });
            if (projectId is null)
                return Results.NotFound(new { error = "Task not found" });

            if (await EndpointHelpers.GetUserProjectRoleAsync(db, projectId.Value, userId) is null)
                return Results.Forbid();

            if (req.AssigneeId.HasValue && await EndpointHelpers.GetUserProjectRoleAsync(db, projectId.Value, req.AssigneeId.Value) is null)
                return Results.BadRequest(new { error = "Assignee must be a member of the project." });

            await db.ExecuteAsync("UPDATE Tasks SET AssigneeId = @AssigneeId WHERE TaskId = @TaskId", new { req.AssigneeId, TaskId = taskId });

            var assigneeName = req.AssigneeId.HasValue
                ? await db.QuerySingleOrDefaultAsync<string>("SELECT FullName FROM Users WHERE UserId = @Id", new { Id = req.AssigneeId.Value })
                : null;

            return Results.Ok(new { taskId, assigneeId = req.AssigneeId, assigneeName });
        });

        // DELETE /api/tasks/{taskId}
        tasks.MapDelete("/{taskId:int}", async (int taskId, ClaimsPrincipal principal, IDbConnectionFactory dbFactory) =>
        {
            var userId = principal.GetCurrentUserId();
            using var db = dbFactory.CreateConnection();

            var projectId = await db.QuerySingleOrDefaultAsync<int?>("SELECT ProjectId FROM Tasks WHERE TaskId = @TaskId", new { TaskId = taskId });
            if (projectId is null)
                return Results.NotFound(new { error = "Task not found" });

            var role = await EndpointHelpers.GetUserProjectRoleAsync(db, projectId.Value, userId);
            if (role is not ("Admin" or "Project Manager"))
                return Results.Forbid();

            await db.ExecuteAsync("DELETE FROM Tasks WHERE TaskId = @TaskId", new { TaskId = taskId });
            return Results.NoContent();
        });

        // POST /api/tasks/{taskId}/tags
        tasks.MapPost("/{taskId:int}/tags", async (int taskId, AssignTagRequest req, ClaimsPrincipal principal, IDbConnectionFactory dbFactory) =>
        {
            var userId = principal.GetCurrentUserId();
            using var db = dbFactory.CreateConnection();

            var projectId = await db.QuerySingleOrDefaultAsync<int?>("SELECT ProjectId FROM Tasks WHERE TaskId = @TaskId", new { TaskId = taskId });
            if (projectId is null)
                return Results.NotFound(new { error = "Task not found" });

            if (await EndpointHelpers.GetUserProjectRoleAsync(db, projectId.Value, userId) is null)
                return Results.Forbid();

            const string sql = """
                IF NOT EXISTS (SELECT 1 FROM TaskTags WHERE TaskId = @TaskId AND TagId = @TagId)
                    INSERT INTO TaskTags (TaskId, TagId) VALUES (@TaskId, @TagId);
                """;

            await db.ExecuteAsync(sql, new { TaskId = taskId, req.TagId });
            return Results.Ok(new { taskId, tagId = req.TagId });
        });

        // DELETE /api/tasks/{taskId}/tags/{tagId}
        tasks.MapDelete("/{taskId:int}/tags/{tagId:int}", async (int taskId, int tagId, ClaimsPrincipal principal, IDbConnectionFactory dbFactory) =>
        {
            var userId = principal.GetCurrentUserId();
            using var db = dbFactory.CreateConnection();

            var projectId = await db.QuerySingleOrDefaultAsync<int?>("SELECT ProjectId FROM Tasks WHERE TaskId = @TaskId", new { TaskId = taskId });
            if (projectId is null)
                return Results.NotFound(new { error = "Task not found" });

            if (await EndpointHelpers.GetUserProjectRoleAsync(db, projectId.Value, userId) is null)
                return Results.Forbid();

            await db.ExecuteAsync("DELETE FROM TaskTags WHERE TaskId = @TaskId AND TagId = @TagId", new { TaskId = taskId, TagId = tagId });
            return Results.NoContent();
        });

        // POST /api/tasks/{taskId}/comments
        tasks.MapPost("/{taskId:int}/comments", async (int taskId, CreateCommentRequest req, ClaimsPrincipal principal, IDbConnectionFactory dbFactory) =>
        {
            if (string.IsNullOrWhiteSpace(req.CommentText))
                return Results.BadRequest(new { error = "CommentText is required." });

            var userId = principal.GetCurrentUserId();
            using var db = dbFactory.CreateConnection();

            var projectId = await db.QuerySingleOrDefaultAsync<int?>("SELECT ProjectId FROM Tasks WHERE TaskId = @TaskId", new { TaskId = taskId });
            if (projectId is null)
                return Results.NotFound(new { error = "Task not found" });

            if (await EndpointHelpers.GetUserProjectRoleAsync(db, projectId.Value, userId) is null)
                return Results.Forbid();

            const string insertSql = """
                INSERT INTO TaskComments (TaskId, UserId, CommentText)
                OUTPUT INSERTED.CommentId, INSERTED.CreatedAt
                VALUES (@TaskId, @UserId, @CommentText);
                """;

            var inserted = await db.QuerySingleAsync<(int CommentId, DateTime CreatedAt)>(insertSql, new
            {
                TaskId = taskId,
                UserId = userId,
                req.CommentText
            });

            var authorName = await db.QuerySingleAsync<string>("SELECT FullName FROM Users WHERE UserId = @UserId", new { UserId = userId });

            var commentDto = new TaskCommentDto(inserted.CommentId, taskId, userId, authorName, req.CommentText, inserted.CreatedAt);
            return Results.Created($"/api/tasks/{taskId}/comments/{inserted.CommentId}", commentDto);
        });

        // Global Tags endpoints
        var tags = app.MapGroup("/api/tags").RequireAuthorization();

        tags.MapGet("/", async (IDbConnectionFactory dbFactory) =>
        {
            using var db = dbFactory.CreateConnection();
            var allTags = await db.QueryAsync<TagEntity>("SELECT TagId, TagName, ColorHex FROM Tags ORDER BY TagName ASC");
            return Results.Ok(allTags);
        });

        tags.MapPost("/", async (CreateTagRequest req, IDbConnectionFactory dbFactory) =>
        {
            using var db = dbFactory.CreateConnection();
            const string sql = """
                INSERT INTO Tags (TagName, ColorHex)
                OUTPUT INSERTED.TagId, INSERTED.TagName, INSERTED.ColorHex
                VALUES (@TagName, @ColorHex);
                """;
            var created = await db.QuerySingleAsync<TagEntity>(sql, new { req.TagName, req.ColorHex });
            return Results.Created($"/api/tags/{created.TagId}", created);
        });
    }

    private sealed record TaskListItemRow(
        int TaskId,
        int ProjectId,
        int? AssigneeId,
        string? AssigneeName,
        int? ParentTaskId,
        string? ParentTaskTitle,
        string Title,
        string? Description,
        string Priority,
        string Status,
        DateTime? DueDate,
        DateTime CreatedAt
    );

    private static async Task<List<TaskListItemDto>> AttachTagsToTasksAsync(IDbConnection db, List<TaskListItemRow> rows)
    {
        if (rows.Count == 0)
            return [];

        var taskIds = rows.Select(r => r.TaskId).ToArray();
        const string tagSql = """
            SELECT tt.TaskId, t.TagId, t.TagName, t.ColorHex
            FROM TaskTags tt
            INNER JOIN Tags t ON tt.TagId = t.TagId
            WHERE tt.TaskId IN @TaskIds;
            """;

        var tagRows = await db.QueryAsync<(int TaskId, int TagId, string TagName, string ColorHex)>(tagSql, new { TaskIds = taskIds });
        var tagsByTask = tagRows
            .GroupBy(x => x.TaskId)
            .ToDictionary(g => g.Key, g => g.Select(x => new TagEntity(x.TagId, x.TagName, x.ColorHex)).ToList());

        return rows.Select(r => new TaskListItemDto(
            r.TaskId, r.ProjectId, r.AssigneeId, r.AssigneeName,
            r.ParentTaskId, r.ParentTaskTitle, r.Title, r.Description,
            r.Priority, r.Status, r.DueDate, r.CreatedAt,
            tagsByTask.GetValueOrDefault(r.TaskId, [])
        )).ToList();
    }

    private static async Task<TaskDetailDto> BuildTaskDetailAsync(IDbConnection db, int taskId)
    {
        var mainRow = await db.QuerySingleAsync<TaskListItemRow>(
            "SELECT * FROM vw_ProjectTaskDetails WHERE TaskId = @TaskId",
            new { TaskId = taskId }
        );

        var mainWithTags = (await AttachTagsToTasksAsync(db, [mainRow]))[0];

        var subtaskRows = (await db.QueryAsync<TaskListItemRow>(
            "SELECT * FROM vw_ProjectTaskDetails WHERE ParentTaskId = @TaskId ORDER BY CreatedAt ASC",
            new { TaskId = taskId }
        )).ToList();

        var subtasks = await AttachTagsToTasksAsync(db, subtaskRows);

        const string commentsSql = """
            SELECT c.CommentId, c.TaskId, c.UserId, u.FullName AS AuthorName, c.CommentText, c.CreatedAt
            FROM TaskComments c
            INNER JOIN Users u ON c.UserId = u.UserId
            WHERE c.TaskId = @TaskId
            ORDER BY c.CreatedAt ASC;
            """;

        var comments = (await db.QueryAsync<TaskCommentDto>(commentsSql, new { TaskId = taskId })).ToList();

        var auditLogs = (await db.QueryAsync<TaskAuditLogEntity>(
            "SELECT * FROM TaskAuditLogs WHERE TaskId = @TaskId ORDER BY ChangedAt DESC",
            new { TaskId = taskId }
        )).ToList();

        return new TaskDetailDto(
            mainWithTags.TaskId, mainWithTags.ProjectId, mainWithTags.AssigneeId, mainWithTags.AssigneeName,
            mainWithTags.ParentTaskId, mainWithTags.ParentTaskTitle, mainWithTags.Title, mainWithTags.Description,
            mainWithTags.Priority, mainWithTags.Status, mainWithTags.DueDate, mainWithTags.CreatedAt,
            mainWithTags.Tags, subtasks, comments, auditLogs
        );
    }
}