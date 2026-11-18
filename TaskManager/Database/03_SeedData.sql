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
        BEGIN TRANSACTION;
        
        INSERT INTO Projects (ProjectName, Description, OwnerId, Deadline)
        VALUES (@ProjectName, @Description, @OwnerId, @Deadline);
        
        DECLARE @NewProjectId INT = SCOPE_IDENTITY();