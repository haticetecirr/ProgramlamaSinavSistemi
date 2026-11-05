using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProgLab.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class RequireExamDuration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_exams_duration",
                table: "exams");

            migrationBuilder.AddCheckConstraint(
                name: "ck_exams_duration",
                table: "exams",
                sql: "kind <> 1 OR (duration_minutes IS NOT NULL AND duration_minutes > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_exams_duration",
                table: "exams");

            migrationBuilder.AddCheckConstraint(
                name: "ck_exams_duration",
                table: "exams",
                sql: "kind <> 1 OR duration_minutes > 0");
        }
    }
}
