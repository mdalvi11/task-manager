namespace TaskManager.Models;

public record UserEntity(
    int UserId,
    string FullName,
    string Email,
    string PasswordHash,
    DateTime CreatedAt
);

public record ProjectEntity(
    int ProjectId,
    string ProjectName,
    string? Description,
    int OwnerId,
    DateTime Deadline,
    string Status,
    DateTime CreatedAt
);

public record TaskEntity(
    int TaskId,
    int ProjectId,
    int? AssigneeId,
    int? ParentTaskId,
    string Title,
    string? Description,
    string Priority,
    string Status,
    DateTime? DueDate,
    DateTime CreatedAt
);

public record TagEntity(
    int TagId,
    string TagName,
    string ColorHex
);

public record TaskAuditLogEntity(
    int LogId,
    int TaskId,
    string FieldChanged,
    string? OldValue,
    string? NewValue,
    DateTime ChangedAt
);
