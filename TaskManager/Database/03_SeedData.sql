USE TaskManagerDB;
GO

-- Seed Users (PasswordHash values are placeholder hashes for 'Password123!')
INSERT INTO Users (FullName, Email, PasswordHash) VALUES
('Ali Raza', 'ali@university.edu', '$2a$11$placeholderhashforaliraza1234567890'),
('Sara Khan', 'sara@university.edu', '$2a$11$placeholderhashforsarakhan123456789'),
('Usman Tariq', 'usman@university.edu', '$2a$11$placeholderhashforusmantariq1234567'),
('Ayesha Malik', 'ayesha@university.edu', '$2a$11$placeholderhashforayeshamalik123456');
GO

-- Seed Tags
INSERT INTO Tags (TagName, ColorHex) VALUES
('Database', '#0EA5E9'),
('Backend', '#8B5CF6'),
('Frontend', '#10B981'),
('Bug', '#EF4444'),
('Urgent', '#F59E0B');
GO

-- Seed Projects via Stored Procedure (auto-adds Owners to ProjectMembers)
EXEC sp_CreateProjectWithOwner 
    @ProjectName = 'University DBMS Portal', 
    @Description = 'Full-stack task management system built with React, .NET 10, and SQL Server.', 
    @OwnerId = 1, 
    @Deadline = '2026-12-15T23:59:59';

EXEC sp_CreateProjectWithOwner 
    @ProjectName = 'Campus Event Scheduler', 
    @Description = 'Internal society event booking and resource allocation system.', 
    @OwnerId = 2, 
    @Deadline = '2026-11-30T18:00:00';
GO

-- Add Additional Members to Projects
INSERT INTO ProjectMembers (ProjectId, UserId, Role) VALUES
(1, 2, 'Project Manager'),
(1, 3, 'Member'),
(1, 4, 'Member'),
(2, 1, 'Member'),
(2, 3, 'Project Manager');
GO

-- Seed Tasks via Stored Procedure (with Tags)
EXEC sp_CreateTaskWithTags 
    @ProjectId = 1, @AssigneeId = 1, @ParentTaskId = NULL,
    @Title = 'Design 3NF Relational Schema', @Description = 'Create tables, PK/FK constraints, and indexes.',
    @Priority = 'Critical', @DueDate = '2026-11-20T17:00:00', @TagIdsCsv = '1';

EXEC sp_CreateTaskWithTags 
    @ProjectId = 1, @AssigneeId = 3, @ParentTaskId = 1,
    @Title = 'Write Audit Trigger on Tasks Table', @Description = 'Log status and assignee updates automatically.',
    @Priority = 'High', @DueDate = '2026-11-21T17:00:00', @TagIdsCsv = '1,2';

EXEC sp_CreateTaskWithTags 
    @ProjectId = 1, @AssigneeId = 2, @ParentTaskId = NULL,
    @Title = 'Build Kanban Board UI in React', @Description = 'Connect drag-and-drop columns to PATCH status endpoint.',
    @Priority = 'High', @DueDate = '2026-11-25T17:00:00', @TagIdsCsv = '3';

EXEC sp_CreateTaskWithTags 
    @ProjectId = 1, @AssigneeId = 4, @ParentTaskId = NULL,
    @Title = 'Fix Cookie CORS Credentials Issue', @Description = 'Ensure credentials: include works on localhost.',
    @Priority = 'Medium', @DueDate = '2026-11-18T12:00:00', @TagIdsCsv = '2,3,4'; -- Intentionally overdue for dashboard demo
GO

-- Seed Comments
INSERT INTO TaskComments (TaskId, UserId, CommentText) VALUES
(1, 2, 'Schema looks clean! Make sure ParentTaskId allows NULL for root tasks.'),
(1, 1, 'Done, verified 3NF across all 8 tables.'),
(4, 3, 'Check AllowCredentials() in ASP.NET Core CORS policy.');
GO

-- Trigger sample updates so TaskAuditLogs has immediate demo data
UPDATE Tasks SET Status = 'Done' WHERE TaskId = 1;
UPDATE Tasks SET Status = 'In Progress', Priority = 'Critical' WHERE TaskId = 2;
GO