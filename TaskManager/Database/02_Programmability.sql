USE TaskManagerDB;
GO

-- Trigger: Automatically log Status, Priority, and Assignee changes on Tasks
CREATE OR ALTER TRIGGER trg_Tasks_AuditUpdate
ON Tasks
AFTER UPDATE
BEGIN
    SET NOCOUNT ON;

    -- Log Status Changes
    IF UPDATE(Status)
        BEGIN
            INSERT INTO TaskAuditLogs (TaskId, FieldChanged, OldValue, NewValue)
            SELECT i.TaskId, 'Status', d.Status, i.Status
            FROM inserted i 
            INNER JOIN deleted d ON i.TaskId = d.TaskId
            WHERE ISNULL(d.Status, '') <> ISNULL(i.Status, '');
        END
    
    -- Log Priority Changes
    IF UPDATE(Priority)
        BEGIN
            INSERT INTO TaskAuditLogs (TaskId, FieldChanged, OldValue, NewValue)
            SELECT i.TaskId, 'Priority', d.Priority, i.Priority
            FROM inserted i
            INNER JOIN deleted d ON i.TaskId = d.TaskId
            WHERE ISNULL(d.Priority, '') <> ISNULL(i.Priority, '');
        END
        
    -- Log Assignee Changes (stores User FullName or 'Unassigned')
    IF UPDATE(AssigneeId)
        BEGIN
            INSERT INTO TaskAuditLogs (TaskId, FieldChanged, OldValue, NewValue)
            SELECT
                i.TaskId,
                'Assignee',
                ISNULL(oldU.FullName, 'Unassigned'),
                ISNULL(newU.FullName, 'Unassigned')
            FROM inserted i
                     INNER JOIN deleted d ON i.TaskId = d.TaskId
                     LEFT JOIN Users oldU ON d.AssigneeId = oldU.UserId
                     LEFT JOIN Users newU ON i.AssigneeId = newU.UserId
            WHERE ISNULL(d.AssigneeId, -1) <> ISNULL(i.AssigneeId, -1);
        END
END;
GO

-- View: Enriched Task Details (Joined with Assignee & Parent Task Title)
CREATE OR ALTER VIEW vw_ProjectTaskDetails AS
SELECT
    t.TaskId,
    t.ProjectId,
    t.AssigneeId,
    u.FullName AS AssigneeName,
    t.ParentTaskId,
    pt.Title AS ParentTaskTitle,
    t.Title,
    t.Description,
    t.Priority,
    t.Status,
    t.DueDate,
    t.CreatedAt
FROM Tasks t
LEFT JOIN Users u ON t.AssigneeId = u.UserId
LEFT JOIN Task pt ON t.ParentTaskId = pt.TaskId;
GO

-- View: Project Summary & Overdue Analytics
CREATE OR ALTER VIEW vw_ProjectSummary AS
SELECT
    p.ProjectId,
    p.ProjectName,
    p.Description,
    p.OwnerId,
    u.FullName AS OwnerName,
    p.Deadline,
    p.Status,
    COUNT(t.TaskId) AS TotalTasks,
    SUM(CASE WHEN t.Status = 'Done' THEN 1 ELSE 0 END) AS CompletedTasks,
    SUM(CASE WHEN t.Status <> 'Done' AND t.DueDate < SYSUTCDATETIME() THEN 1 ELSE 0 END) AS OverdueTasks
FROM Projects p
INNER JOIN Users u ON p.OwnerId = u.UserId
LEFT JOIN Tasks t ON p.ProjectId = t.ProjectId
GROUP BY p.ProjectId, p.ProjectName, p.Description, p.OwnerId, u.FullName, p.Deadline, p.Status;
GO

-- View: User Workload Distribution
CREATE OR ALTER VIEW vw_UserWorkload AS
SELECT
    u.UserId,
    u.FullName,
    u.Email,
    COUNT(t.TaskId) AS AssignedTasks,
    SUM(CASE WHEN t.Status = 'Done' THEN 1 ELSE 0 END) AS CompletedTasks,
    SUM(CASE WHEN t.Status IN ('To Do', 'In Progress', 'In Review') THEN 1 ELSE 0 END) AS ActiveTasks,
    SUM(CASE WHEN t.Status <> 'Done' AND t.DueDate < SYSUTCDATETIME() THEN 1 ELSE 0 END) AS OverdueTasks
FROM Users u
LEFT JOIN Tasks t ON u.UserId = t.AssigneeId
GROUP BY u.UserId, u.FullName, u.Email;
GO
