using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmployeeData.Migrations
{
    /// <inheritdoc />
    public partial class EnforceBoundedDomainValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1 FROM [Users]
                    WHERE [UserName] IS NULL
                       OR TRIM([UserName]) = ''
                       OR DATALENGTH([UserName]) > 200)
                BEGIN
                    THROW 51001, 'Cannot enforce user-name constraints. Remove blank names and shorten names to 100 characters, then retry the migration.', 1;
                END;

                IF EXISTS (
                    SELECT [UserName] FROM [Users]
                    GROUP BY [UserName]
                    HAVING COUNT(*) > 1)
                BEGIN
                    THROW 51002, 'Cannot create the unique user-name index while duplicate names exist. Rename duplicate users, then retry the migration.', 1;
                END;

                IF EXISTS (
                    SELECT 1 FROM [Users]
                    WHERE [Role] IS NULL
                       OR [Role] NOT IN ('Administrator', 'Employee')
                       OR DATALENGTH([Role]) > 64)
                BEGIN
                    THROW 51003, 'Cannot enforce role constraints. Every role must be Administrator or Employee, then retry the migration.', 1;
                END;

                IF EXISTS (
                    SELECT 1 FROM [Users]
                    WHERE [ProfilePictureUrl] IS NOT NULL
                      AND DATALENGTH([ProfilePictureUrl]) > 4096)
                BEGIN
                    THROW 51004, 'Cannot limit profile-picture URLs while values longer than 2048 characters exist. Correct those values, then retry the migration.', 1;
                END;

                IF EXISTS (
                    SELECT 1 FROM [Projects]
                    WHERE [Name] IS NULL
                       OR TRIM([Name]) = ''
                       OR DATALENGTH([Name]) > 200
                       OR ([Description] IS NOT NULL AND DATALENGTH([Description]) > 2000))
                BEGIN
                    THROW 51005, 'Cannot enforce project limits. Project names must be nonblank and at most 100 characters; descriptions must be at most 1000 characters.', 1;
                END;

                IF EXISTS (
                    SELECT 1 FROM [Tasks]
                    WHERE [Title] IS NULL
                       OR TRIM([Title]) = ''
                       OR DATALENGTH([Title]) > 400
                       OR ([Description] IS NOT NULL AND DATALENGTH([Description]) > 4000))
                BEGIN
                    THROW 51006, 'Cannot enforce task limits. Task titles must be nonblank and at most 200 characters; descriptions must be at most 2000 characters.', 1;
                END;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "UserName",
                table: "Users",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Role",
                table: "Users",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ProfilePictureUrl",
                table: "Users",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "Tasks",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Tasks",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Projects",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Projects",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_UserName",
                table: "Users",
                column: "UserName",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_Role",
                table: "Users",
                sql: "[Role] IN ('Administrator', 'Employee')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_UserName_NotBlank",
                table: "Users",
                sql: "TRIM([UserName]) <> ''");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tasks_Title_NotBlank",
                table: "Tasks",
                sql: "TRIM([Title]) <> ''");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Projects_Name_NotBlank",
                table: "Projects",
                sql: "TRIM([Name]) <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_UserName",
                table: "Users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_Role",
                table: "Users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_UserName_NotBlank",
                table: "Users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tasks_Title_NotBlank",
                table: "Tasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Projects_Name_NotBlank",
                table: "Projects");

            migrationBuilder.AlterColumn<string>(
                name: "UserName",
                table: "Users",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "Role",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "ProfilePictureUrl",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(2048)",
                oldMaxLength: 2048,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "Tasks",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Tasks",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Projects",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Projects",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000,
                oldNullable: true);
        }
    }
}
