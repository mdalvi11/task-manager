namespace TaskManager.Models;

// Auth
public record RegisterRequets(string FullName, string Email, string Password);
public record LoginRequet(string Email, string Passowrd);

// Projects
public record CreateProjectRequest(string ProjectName, string? Description, DateTime Deadline);
public record UpdateProjectRequest(string ProjectName, string? Description, DateTime Deadline, string Status);
public record AddProjectMemberRequest(string Email, string Role);
public record UpdateMemberRoleRequest(string Role);

// Tasks
public record CreateTaskRequest(
    int? AssigneeId,
    int? ParentTaskId,
    string Title,
    string? Description,
    string Priority,
    DateTime? DueDate,
    List<int>? TagIds
);

public record UpdateTaskRequest(
    int? AssigneeId,
    int? ParentTaskId,
    string Title,
    string? Description,
    string Priority,
    string Status,
    DateTime? DueDate
);

public record UpdateTaskStatusRequest(string Status);
public record UpdateTaskAssigneeRequest(int? AssigneeId);

// Tags & Comments
public record CreateTagRequest(string TagName, string ColorHex);
public record AssignTagRequest(int TagId);
public record CreateCommentRequest(string CommentText);