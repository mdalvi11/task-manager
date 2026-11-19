namespace TaskManager.Models;

// Auth
public record UserDto(int UserId, string FullName, string Email, DateTime CreatedAt);
public record AuthResponse(string Message, UserDto User);

// Projects & Members
public record ProjectSummaryDto(
    int ProjectId,
    string ProjectName,
    string? Description,
    int OwnerId,
    string OwnerName,
    DateTime Deadline,
    string Status,
    int TotalTasks,
    int CompletedTasks,
    int OverdueTasks
);

public record ProjectMemberDto(
    int UserId,
    string FullName,
    string Email,
    string Role,
    DateTime JoinedAt
);

public record ProjectDetailDto(
    int ProjectId,
    string ProjectName,
    string? Description,
    int OwnerId,
    string OwnerName,
    DateTime Deadline,
    string Status,
    int TotalTasks,
    int CompletedTasks,
    int OverdueTasks,
    List<ProjectMemberDto> Members
);

// Tasks, Comments & Audit Logs
public record TaskListItemDto(
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
    DateTime CreatedAt,
    List<TagEntity> Tags
);

public record TaskCommentDto(
    int CommentId,
    int TaskId,
    int UserId,
    string AuthorName,
    string CommentText,
    DateTime CreatedAt
);

public record TaskDetailDto(
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
    DateTime CreatedAt,
    List<TagEntity> Tags,
    List<TaskListItemDto> Subtasks,
    List<TaskCommentDto> Comments,
    List<TaskAuditLogEntity> AuditLogs
);

// Analytics
public record UserWorkloadDto(
    int UserId,
    string FullName,
    string Email,
    int AssignedTasks,
    int CompletedTasks,
    int ActiveTasks,
    int OverdueTasks
);

public record DashboardOverviewDto(
    int TotalProjects,
    int ActiveProjects,
    int TotalTasks,
    int CompletedTasks,
    int OverdueTasks,
    List<ProjectSummaryDto> Projects,
    List<UserWorkloadDto> TeamWorkload
);