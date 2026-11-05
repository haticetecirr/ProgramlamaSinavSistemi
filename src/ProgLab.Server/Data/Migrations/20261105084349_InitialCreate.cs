using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProgLab.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    parent_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categories", x => x.id);
                    table.ForeignKey(
                        name: "fk_categories_categories_parent_id",
                        column: x => x.parent_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    username = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    full_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    role = table.Column<short>(type: "smallint", nullable: false),
                    password_hash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    must_change_password = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    external_ref = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "classes",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    term = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    instructor_id = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_classes", x => x.id);
                    table.ForeignKey(
                        name: "fk_classes_users_instructor_id",
                        column: x => x.instructor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "questions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    category_id = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<short>(type: "smallint", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    default_points = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    version_no = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    previous_version_id = table.Column<int>(type: "integer", nullable: true),
                    is_current = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    is_locked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_by = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questions", x => x.id);
                    table.CheckConstraint("ck_questions_default_points", "default_points > 0");
                    table.ForeignKey(
                        name: "fk_questions_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_questions_questions_previous_version_id",
                        column: x => x.previous_version_id,
                        principalTable: "questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_questions_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "class_members",
                columns: table => new
                {
                    class_id = table.Column<int>(type: "integer", nullable: false),
                    student_id = table.Column<int>(type: "integer", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_class_members", x => new { x.class_id, x.student_id });
                    table.ForeignKey(
                        name: "fk_class_members_classes_class_id",
                        column: x => x.class_id,
                        principalTable: "classes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_class_members_users_student_id",
                        column: x => x.student_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exams",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    class_id = table.Column<int>(type: "integer", nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: true),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)1),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    results_published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exams", x => x.id);
                    table.CheckConstraint("ck_exams_duration", "kind <> 1 OR duration_minutes > 0");
                    table.CheckConstraint("ck_exams_published_has_times", "status = 1 OR (starts_at IS NOT NULL AND ends_at IS NOT NULL)");
                    table.CheckConstraint("ck_exams_time_window", "ends_at IS NULL OR starts_at < ends_at");
                    table.ForeignKey(
                        name: "fk_exams_classes_class_id",
                        column: x => x.class_id,
                        principalTable: "classes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_exams_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "classic_answer_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    question_id = table.Column<int>(type: "integer", nullable: false),
                    accepted_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    normalized_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_classic_answer_keys", x => x.id);
                    table.ForeignKey(
                        name: "fk_classic_answer_keys_questions_question_id",
                        column: x => x.question_id,
                        principalTable: "questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "question_options",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    question_id = table.Column<int>(type: "integer", nullable: false),
                    order_no = table.Column<short>(type: "smallint", nullable: false),
                    text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    is_correct = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_question_options", x => x.id);
                    table.ForeignKey(
                        name: "fk_question_options_questions_question_id",
                        column: x => x.question_id,
                        principalTable: "questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "exam_questions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    exam_id = table.Column<int>(type: "integer", nullable: false),
                    question_id = table.Column<int>(type: "integer", nullable: false),
                    order_no = table.Column<short>(type: "smallint", nullable: false),
                    points = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exam_questions", x => x.id);
                    table.CheckConstraint("ck_exam_questions_points", "points > 0");
                    table.ForeignKey(
                        name: "fk_exam_questions_exams_exam_id",
                        column: x => x.exam_id,
                        principalTable: "exams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_exam_questions_questions_question_id",
                        column: x => x.question_id,
                        principalTable: "questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "submissions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    exam_id = table.Column<int>(type: "integer", nullable: false),
                    student_id = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_saved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    submit_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    total_score = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: true),
                    graded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    client_ip = table.Column<IPAddress>(type: "inet", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_submissions", x => x.id);
                    table.ForeignKey(
                        name: "fk_submissions_exams_exam_id",
                        column: x => x.exam_id,
                        principalTable: "exams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_submissions_users_student_id",
                        column: x => x.student_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "activity_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    event_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: true),
                    exam_id = table.Column<int>(type: "integer", nullable: true),
                    submission_id = table.Column<long>(type: "bigint", nullable: true),
                    ip = table.Column<IPAddress>(type: "inet", nullable: true),
                    details = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_activity_logs_exams_exam_id",
                        column: x => x.exam_id,
                        principalTable: "exams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_activity_logs_submissions_submission_id",
                        column: x => x.submission_id,
                        principalTable: "submissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_activity_logs_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "answers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    submission_id = table.Column<long>(type: "bigint", nullable: false),
                    exam_question_id = table.Column<int>(type: "integer", nullable: false),
                    selected_option_id = table.Column<int>(type: "integer", nullable: true),
                    text_answer = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    score = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    graded_by = table.Column<short>(type: "smallint", nullable: true),
                    graded_note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_answers", x => x.id);
                    table.ForeignKey(
                        name: "fk_answers_exam_questions_exam_question_id",
                        column: x => x.exam_question_id,
                        principalTable: "exam_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_answers_question_options_selected_option_id",
                        column: x => x.selected_option_id,
                        principalTable: "question_options",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_answers_submissions_submission_id",
                        column: x => x.submission_id,
                        principalTable: "submissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_activity_logs_event_type",
                table: "activity_logs",
                column: "event_type");

            migrationBuilder.CreateIndex(
                name: "ix_activity_logs_exam_id_occurred_at",
                table: "activity_logs",
                columns: new[] { "exam_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_activity_logs_submission_id",
                table: "activity_logs",
                column: "submission_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_logs_user_id_occurred_at",
                table: "activity_logs",
                columns: new[] { "user_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_answers_exam_question_id",
                table: "answers",
                column: "exam_question_id");

            migrationBuilder.CreateIndex(
                name: "ix_answers_selected_option_id",
                table: "answers",
                column: "selected_option_id");

            migrationBuilder.CreateIndex(
                name: "ix_answers_submission_id_exam_question_id",
                table: "answers",
                columns: new[] { "submission_id", "exam_question_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_categories_parent_id_name",
                table: "categories",
                columns: new[] { "parent_id", "name" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_class_members_student_id",
                table: "class_members",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ix_classes_instructor_id",
                table: "classes",
                column: "instructor_id");

            migrationBuilder.CreateIndex(
                name: "ix_classes_name_term",
                table: "classes",
                columns: new[] { "name", "term" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_classic_answer_keys_question_id_normalized_text",
                table: "classic_answer_keys",
                columns: new[] { "question_id", "normalized_text" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_exam_questions_exam_id_order_no",
                table: "exam_questions",
                columns: new[] { "exam_id", "order_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_exam_questions_exam_id_question_id",
                table: "exam_questions",
                columns: new[] { "exam_id", "question_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_exam_questions_question_id",
                table: "exam_questions",
                column: "question_id");

            migrationBuilder.CreateIndex(
                name: "ix_exams_class_id_status",
                table: "exams",
                columns: new[] { "class_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_exams_created_by",
                table: "exams",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_exams_status_ends_at",
                table: "exams",
                columns: new[] { "status", "ends_at" });

            migrationBuilder.CreateIndex(
                name: "ix_question_options_question_id_order_no",
                table: "question_options",
                columns: new[] { "question_id", "order_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_question_options_single_correct",
                table: "question_options",
                column: "question_id",
                unique: true,
                filter: "is_correct");

            migrationBuilder.CreateIndex(
                name: "ix_questions_category_id",
                table: "questions",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_questions_category_id_current",
                table: "questions",
                column: "category_id",
                filter: "is_current AND NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_questions_created_by",
                table: "questions",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_questions_previous_version_id",
                table: "questions",
                column: "previous_version_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_questions_type",
                table: "questions",
                column: "type");

            migrationBuilder.CreateIndex(
                name: "ix_submissions_exam_id_status",
                table: "submissions",
                columns: new[] { "exam_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_submissions_exam_id_student_id",
                table: "submissions",
                columns: new[] { "exam_id", "student_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_submissions_student_id",
                table: "submissions",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_role",
                table: "users",
                column: "role");

            migrationBuilder.CreateIndex(
                name: "ix_users_username",
                table: "users",
                column: "username",
                unique: true);

            // EF modeli ifade indeksini tanimlayamadigi icin elle: e-posta buyuk/kucuk harf duyarsiz benzersiz.
            migrationBuilder.Sql("CREATE UNIQUE INDEX ix_users_email_lower ON users (lower(email));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activity_logs");

            migrationBuilder.DropTable(
                name: "answers");

            migrationBuilder.DropTable(
                name: "class_members");

            migrationBuilder.DropTable(
                name: "classic_answer_keys");

            migrationBuilder.DropTable(
                name: "exam_questions");

            migrationBuilder.DropTable(
                name: "question_options");

            migrationBuilder.DropTable(
                name: "submissions");

            migrationBuilder.DropTable(
                name: "questions");

            migrationBuilder.DropTable(
                name: "exams");

            migrationBuilder.DropTable(
                name: "categories");

            migrationBuilder.DropTable(
                name: "classes");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
