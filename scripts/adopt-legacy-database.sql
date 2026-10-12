/*
Safely adopts the exact schema produced by the repository's historical
EnsureCreated startup path into EF Core migration history.

Run this script against the intended EmployeeData database before applying
new migrations. It never changes application tables and refuses any schema
that is not structurally compatible with the original migration.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @BaselineMigrationId nvarchar(150) = N'20240120132125_NewInitialCreation';
DECLARE @BaselineProductVersion nvarchar(32) = N'5.0.17';

IF OBJECT_ID(N'[dbo].[__EFMigrationsHistory]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.__EFMigrationsHistory', N'MigrationId') IS NULL
       OR COL_LENGTH(N'dbo.__EFMigrationsHistory', N'ProductVersion') IS NULL
    BEGIN
        ;THROW 51000, N'The existing __EFMigrationsHistory table has an unexpected shape. No changes were made.', 1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM [dbo].[__EFMigrationsHistory]
        WHERE [MigrationId] = @BaselineMigrationId
    )
    BEGIN
        PRINT N'The legacy baseline migration is already recorded. No changes were needed.';
        RETURN;
    END;

    ;THROW 51000, N'Migration history exists but does not contain the expected baseline. No changes were made.', 1;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @ExpectedColumns table
    (
        [TableName] sysname NOT NULL,
        [ColumnName] sysname NOT NULL,
        [TypeName] sysname NOT NULL,
        [MaxLength] smallint NOT NULL,
        [IsNullable] bit NOT NULL,
        [IsIdentity] bit NOT NULL,
        PRIMARY KEY ([TableName], [ColumnName])
    );

    INSERT INTO @ExpectedColumns
        ([TableName], [ColumnName], [TypeName], [MaxLength], [IsNullable], [IsIdentity])
    VALUES
        (N'Projects', N'Id', N'int', 4, 0, 1),
        (N'Projects', N'Name', N'nvarchar', -1, 1, 0),
        (N'Projects', N'Description', N'nvarchar', -1, 1, 0),
        (N'Users', N'Id', N'int', 4, 0, 1),
        (N'Users', N'UserName', N'nvarchar', -1, 0, 0),
        (N'Users', N'Password', N'nvarchar', -1, 0, 0),
        (N'Users', N'Role', N'nvarchar', -1, 1, 0),
        (N'Users', N'ProfilePictureUrl', N'nvarchar', -1, 1, 0),
        (N'Tasks', N'Id', N'int', 4, 0, 1),
        (N'Tasks', N'Title', N'nvarchar', -1, 0, 0),
        (N'Tasks', N'Description', N'nvarchar', -1, 1, 0),
        (N'Tasks', N'IsFinished', N'bit', 1, 0, 0),
        (N'Tasks', N'ProjectId', N'int', 4, 1, 0),
        (N'UserProjects', N'UserId', N'int', 4, 0, 0),
        (N'UserProjects', N'ProjectId', N'int', 4, 0, 0),
        (N'UserProjects', N'Id', N'int', 4, 0, 0),
        (N'UserTasks', N'UserId', N'int', 4, 0, 0),
        (N'UserTasks', N'TaskId', N'int', 4, 0, 0),
        (N'UserTasks', N'Id', N'int', 4, 0, 0);

    IF EXISTS
    (
        SELECT 1
        FROM @ExpectedColumns AS expected
        LEFT JOIN sys.schemas AS schemaInfo
            ON schemaInfo.[name] = N'dbo'
        LEFT JOIN sys.tables AS tableInfo
            ON tableInfo.[schema_id] = schemaInfo.[schema_id]
           AND tableInfo.[name] = expected.[TableName]
        LEFT JOIN sys.columns AS columnInfo
            ON columnInfo.[object_id] = tableInfo.[object_id]
           AND columnInfo.[name] = expected.[ColumnName]
        LEFT JOIN sys.types AS typeInfo
            ON typeInfo.[user_type_id] = columnInfo.[user_type_id]
        WHERE columnInfo.[column_id] IS NULL
           OR typeInfo.[name] <> expected.[TypeName]
           OR columnInfo.[max_length] <> expected.[MaxLength]
           OR columnInfo.[is_nullable] <> expected.[IsNullable]
           OR columnInfo.[is_identity] <> expected.[IsIdentity]
    )
    BEGIN
        ;THROW 51000, N'The legacy database is missing an expected table or column, or a column definition differs. No changes were made.', 1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM sys.schemas AS schemaInfo
        INNER JOIN sys.tables AS tableInfo
            ON tableInfo.[schema_id] = schemaInfo.[schema_id]
        INNER JOIN sys.columns AS columnInfo
            ON columnInfo.[object_id] = tableInfo.[object_id]
        WHERE schemaInfo.[name] = N'dbo'
          AND tableInfo.[name] IN (N'Projects', N'Users', N'Tasks', N'UserProjects', N'UserTasks')
          AND NOT EXISTS
          (
              SELECT 1
              FROM @ExpectedColumns AS expected
              WHERE expected.[TableName] = tableInfo.[name]
                AND expected.[ColumnName] = columnInfo.[name]
          )
    )
    BEGIN
        ;THROW 51000, N'The legacy database contains unexpected columns in an application table. No changes were made.', 1;
    END;

    DECLARE @ExpectedPrimaryKeys table
    (
        [ConstraintName] sysname NOT NULL,
        [TableName] sysname NOT NULL,
        [ColumnName] sysname NOT NULL,
        [KeyOrdinal] tinyint NOT NULL,
        PRIMARY KEY ([TableName], [KeyOrdinal])
    );

    INSERT INTO @ExpectedPrimaryKeys
        ([ConstraintName], [TableName], [ColumnName], [KeyOrdinal])
    VALUES
        (N'PK_Projects', N'Projects', N'Id', 1),
        (N'PK_Users', N'Users', N'Id', 1),
        (N'PK_Tasks', N'Tasks', N'Id', 1),
        (N'PK_UserProjects', N'UserProjects', N'UserId', 1),
        (N'PK_UserProjects', N'UserProjects', N'ProjectId', 2),
        (N'PK_UserTasks', N'UserTasks', N'UserId', 1),
        (N'PK_UserTasks', N'UserTasks', N'TaskId', 2);

    IF EXISTS
    (
        SELECT 1
        FROM @ExpectedPrimaryKeys AS expected
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM sys.schemas AS schemaInfo
            INNER JOIN sys.tables AS tableInfo
                ON tableInfo.[schema_id] = schemaInfo.[schema_id]
            INNER JOIN sys.key_constraints AS keyInfo
                ON keyInfo.[parent_object_id] = tableInfo.[object_id]
               AND keyInfo.[type] = N'PK'
            INNER JOIN sys.indexes AS indexInfo
                ON indexInfo.[object_id] = tableInfo.[object_id]
               AND indexInfo.[index_id] = keyInfo.[unique_index_id]
            INNER JOIN sys.index_columns AS indexColumn
                ON indexColumn.[object_id] = tableInfo.[object_id]
               AND indexColumn.[index_id] = keyInfo.[unique_index_id]
            INNER JOIN sys.columns AS columnInfo
                ON columnInfo.[object_id] = tableInfo.[object_id]
               AND columnInfo.[column_id] = indexColumn.[column_id]
            WHERE schemaInfo.[name] = N'dbo'
              AND tableInfo.[name] = expected.[TableName]
              AND keyInfo.[name] = expected.[ConstraintName]
              AND columnInfo.[name] = expected.[ColumnName]
              AND indexColumn.[key_ordinal] = expected.[KeyOrdinal]
              AND indexInfo.[is_disabled] = 0
              AND indexInfo.[is_hypothetical] = 0
        )
    )
    OR EXISTS
    (
        SELECT 1
        FROM sys.schemas AS schemaInfo
        INNER JOIN sys.tables AS tableInfo
            ON tableInfo.[schema_id] = schemaInfo.[schema_id]
        INNER JOIN sys.key_constraints AS keyInfo
            ON keyInfo.[parent_object_id] = tableInfo.[object_id]
           AND keyInfo.[type] = N'PK'
        INNER JOIN sys.index_columns AS indexColumn
            ON indexColumn.[object_id] = tableInfo.[object_id]
           AND indexColumn.[index_id] = keyInfo.[unique_index_id]
        INNER JOIN sys.columns AS columnInfo
            ON columnInfo.[object_id] = tableInfo.[object_id]
           AND columnInfo.[column_id] = indexColumn.[column_id]
        WHERE schemaInfo.[name] = N'dbo'
          AND tableInfo.[name] IN (N'Projects', N'Users', N'Tasks', N'UserProjects', N'UserTasks')
          AND NOT EXISTS
          (
              SELECT 1
              FROM @ExpectedPrimaryKeys AS expected
              WHERE expected.[TableName] = tableInfo.[name]
                AND expected.[ConstraintName] = keyInfo.[name]
                AND expected.[ColumnName] = columnInfo.[name]
                AND expected.[KeyOrdinal] = indexColumn.[key_ordinal]
          )
    )
    BEGIN
        ;THROW 51000, N'The legacy database primary keys do not match the expected schema. No changes were made.', 1;
    END;

    DECLARE @ExpectedForeignKeys table
    (
        [ConstraintName] sysname NOT NULL PRIMARY KEY,
        [TableName] sysname NOT NULL,
        [ColumnName] sysname NOT NULL,
        [ReferencedTableName] sysname NOT NULL,
        [ReferencedColumnName] sysname NOT NULL,
        [DeleteAction] nvarchar(60) NOT NULL
    );

    INSERT INTO @ExpectedForeignKeys
        ([ConstraintName], [TableName], [ColumnName], [ReferencedTableName], [ReferencedColumnName], [DeleteAction])
    VALUES
        (N'FK_Tasks_Projects_ProjectId', N'Tasks', N'ProjectId', N'Projects', N'Id', N'NO_ACTION'),
        (N'FK_UserProjects_Projects_ProjectId', N'UserProjects', N'ProjectId', N'Projects', N'Id', N'CASCADE'),
        (N'FK_UserProjects_Users_UserId', N'UserProjects', N'UserId', N'Users', N'Id', N'CASCADE'),
        (N'FK_UserTasks_Tasks_TaskId', N'UserTasks', N'TaskId', N'Tasks', N'Id', N'CASCADE'),
        (N'FK_UserTasks_Users_UserId', N'UserTasks', N'UserId', N'Users', N'Id', N'CASCADE');

    IF EXISTS
    (
        SELECT 1
        FROM @ExpectedForeignKeys AS expected
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM sys.foreign_keys AS foreignKey
            INNER JOIN sys.foreign_key_columns AS foreignKeyColumn
                ON foreignKeyColumn.[constraint_object_id] = foreignKey.[object_id]
            INNER JOIN sys.tables AS tableInfo
                ON tableInfo.[object_id] = foreignKey.[parent_object_id]
            INNER JOIN sys.schemas AS schemaInfo
                ON schemaInfo.[schema_id] = tableInfo.[schema_id]
            INNER JOIN sys.columns AS columnInfo
                ON columnInfo.[object_id] = tableInfo.[object_id]
               AND columnInfo.[column_id] = foreignKeyColumn.[parent_column_id]
            INNER JOIN sys.tables AS referencedTable
                ON referencedTable.[object_id] = foreignKey.[referenced_object_id]
            INNER JOIN sys.schemas AS referencedSchema
                ON referencedSchema.[schema_id] = referencedTable.[schema_id]
            INNER JOIN sys.columns AS referencedColumn
                ON referencedColumn.[object_id] = referencedTable.[object_id]
               AND referencedColumn.[column_id] = foreignKeyColumn.[referenced_column_id]
            WHERE schemaInfo.[name] = N'dbo'
              AND foreignKey.[name] = expected.[ConstraintName]
              AND tableInfo.[name] = expected.[TableName]
              AND columnInfo.[name] = expected.[ColumnName]
              AND referencedSchema.[name] = N'dbo'
              AND referencedTable.[name] = expected.[ReferencedTableName]
              AND referencedColumn.[name] = expected.[ReferencedColumnName]
              AND foreignKey.[delete_referential_action_desc] COLLATE DATABASE_DEFAULT = expected.[DeleteAction]
              AND foreignKey.[is_disabled] = 0
              AND foreignKey.[is_not_trusted] = 0
              AND
              (
                  SELECT COUNT(*)
                  FROM sys.foreign_key_columns AS mappingCount
                  WHERE mappingCount.[constraint_object_id] = foreignKey.[object_id]
              ) = 1
        )
    )
    OR EXISTS
    (
        SELECT 1
        FROM sys.foreign_keys AS foreignKey
        INNER JOIN sys.tables AS tableInfo
            ON tableInfo.[object_id] = foreignKey.[parent_object_id]
        INNER JOIN sys.schemas AS schemaInfo
            ON schemaInfo.[schema_id] = tableInfo.[schema_id]
        WHERE schemaInfo.[name] = N'dbo'
          AND tableInfo.[name] IN (N'Projects', N'Users', N'Tasks', N'UserProjects', N'UserTasks')
          AND NOT EXISTS
          (
              SELECT 1
              FROM @ExpectedForeignKeys AS expected
              WHERE expected.[ConstraintName] = foreignKey.[name]
          )
    )
    BEGIN
        ;THROW 51000, N'The legacy database foreign keys do not match the expected schema. No changes were made.', 1;
    END;

    DECLARE @ExpectedIndexes table
    (
        [IndexName] sysname NOT NULL PRIMARY KEY,
        [TableName] sysname NOT NULL,
        [ColumnName] sysname NOT NULL
    );

    INSERT INTO @ExpectedIndexes ([IndexName], [TableName], [ColumnName])
    VALUES
        (N'IX_Tasks_ProjectId', N'Tasks', N'ProjectId'),
        (N'IX_UserProjects_ProjectId', N'UserProjects', N'ProjectId'),
        (N'IX_UserTasks_TaskId', N'UserTasks', N'TaskId');

    IF EXISTS
    (
        SELECT 1
        FROM @ExpectedIndexes AS expected
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM sys.schemas AS schemaInfo
            INNER JOIN sys.tables AS tableInfo
                ON tableInfo.[schema_id] = schemaInfo.[schema_id]
            INNER JOIN sys.indexes AS indexInfo
                ON indexInfo.[object_id] = tableInfo.[object_id]
            INNER JOIN sys.index_columns AS indexColumn
                ON indexColumn.[object_id] = tableInfo.[object_id]
               AND indexColumn.[index_id] = indexInfo.[index_id]
               AND indexColumn.[key_ordinal] = 1
            INNER JOIN sys.columns AS columnInfo
                ON columnInfo.[object_id] = tableInfo.[object_id]
               AND columnInfo.[column_id] = indexColumn.[column_id]
            WHERE schemaInfo.[name] = N'dbo'
              AND tableInfo.[name] = expected.[TableName]
              AND indexInfo.[name] = expected.[IndexName]
              AND columnInfo.[name] = expected.[ColumnName]
              AND indexInfo.[is_unique] = 0
              AND indexInfo.[is_primary_key] = 0
              AND indexInfo.[is_unique_constraint] = 0
              AND indexInfo.[has_filter] = 0
              AND indexInfo.[is_disabled] = 0
              AND indexInfo.[is_hypothetical] = 0
              AND indexColumn.[is_descending_key] = 0
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM sys.index_columns AS extraIndexColumn
                  WHERE extraIndexColumn.[object_id] = indexInfo.[object_id]
                    AND extraIndexColumn.[index_id] = indexInfo.[index_id]
                    AND (extraIndexColumn.[key_ordinal] > 1 OR extraIndexColumn.[is_included_column] = 1)
              )
        )
    )
    BEGIN
        ;THROW 51000, N'The legacy database is missing an expected index or an index definition differs. No changes were made.', 1;
    END;

    CREATE TABLE [dbo].[__EFMigrationsHistory]
    (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );

    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (@BaselineMigrationId, @BaselineProductVersion);

    COMMIT TRANSACTION;
    PRINT N'The legacy database schema matched and the baseline migration was recorded.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
