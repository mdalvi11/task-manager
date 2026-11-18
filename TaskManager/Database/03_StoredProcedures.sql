USE TaskManagerDB;
GO

-- Create Project and automatically add Owner as 'Admin' in ProjectMembers
CREATE OR ALTER PROCEDURE sp_CreateProjectWithOwner
    @ProjectName NVARCHAR(150)
    @Description NVARCHAR(500) = NULL,
    @OwnerId INT,
    @Deadline DATETIME2
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION ;

        INSERT INTO Projects (ProjectName, Description, OwnerId, Deadline)
        VALUES (@ProjectName, @Description, @OwnerId, @Deadline);
        
        DECLARE @NewProjectId INT = SCOPE_IDENTITY();
        
        INSERT INTO ProjectMembers (ProjectId, UserId, Role)
        VALUES (@NewProjectId, @OwnerId, 'Admin');
        
        COMMIT TRANSACTION;

        SELECT @NewProjectId AS ProjectId;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

-- Create Task and optionally attach comma-separated TagIds (e.g., '1,3,4')
CREATE OR ALTER PROCEDURE sp_CreateTaskWithTags
    @ProjectId INT,
    @AssigneeId INT = NULL,
    @ParentTaskId INT = NULL,
    @Title NVARCHAR(200),
    @Description NVARCHAR(1000) = NULL,
    @Priority NVARCHAR(20) = 'Medium',
    @DueDate DATETIME2 = NULL,
    @TagIdsCsv NVARCHAR(200) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
    BEGIN TRANSACTION;
        
        -- Validate Assignee belongs to the Project if provided
        IF @AssigneeId IS NOT NULL AND NOT EXISTS (
           SELECT 1 FROM ProjectMembers WHERE ProjectId = @ProjectId AND UserId = @AssigneeId
        )
BEGIN
        THROW 50001, 'Assignee is not a member of this project.', 1;
END

        INSERT INTO Tasks (ProjectId, AssigneeId, ParentTaskId, Title, Description, Priority, DueDate)
        VALUES (@ProjectId, @AssigneeId, @ParentTaskId, @Title, @Description, @Priority, @DueDate);
        
        DECLARE @NewTaskId INT = SCOPE_IDENTITY();
                
        IF @TagIdsCsv IS NOT NULL AND LEN(TRIM(@TagIdsCsv)) > 0
        BEGIN
            INSERT INTO TaskTags (TaskId, TagId)
            SELECT DISTINCT @NewTaskId, TRY_CAST(TRIM(value) AS INT)
            FROM STRING_SPLIT(@TagIdsCsv, ',')
            WHERE TRY_CAST(TRIM(value) AS INT) IS NOT NULL;
        END

        COMMIT TRANSACTION;

        SELECT @NewTaskId AS TaskId;
    END TRY
    BEGIN CATCH
    IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO