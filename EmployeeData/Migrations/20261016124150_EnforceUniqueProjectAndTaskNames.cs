using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmployeeData.Migrations
{
    /// <inheritdoc />
    public partial class EnforceUniqueProjectAndTaskNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql(
                    """
                    IF EXISTS (
                        SELECT 1
                        FROM [Projects]
                        GROUP BY UPPER(LTRIM(RTRIM([Name])))
                        HAVING COUNT(*) > 1)
                    BEGIN
                        THROW 51000, 'Cannot enforce unique project names while duplicate normalized names exist.', 1;
                    END;

                    IF EXISTS (
                        SELECT 1
                        FROM [Tasks]
                        GROUP BY UPPER(LTRIM(RTRIM([Title])))
                        HAVING COUNT(*) > 1)
                    BEGIN
                        THROW 51000, 'Cannot enforce unique task titles while duplicate normalized titles exist.', 1;
                    END;
                    """);
            }

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_Title",
                table: "Tasks",
                column: "Title",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Name",
                table: "Projects",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tasks_Title",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Projects_Name",
                table: "Projects");
        }
    }
}
