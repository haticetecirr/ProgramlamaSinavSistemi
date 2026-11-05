using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProgLab.Server.Tests.Infrastructure;

namespace ProgLab.Server.Tests;

/// <summary>Migration'ın ürettiği gerçek şemayı dokümandaki "Tablolar" bölümüyle karşılaştırır.</summary>
public sealed class DatabaseSchemaTests(MigratedDatabaseFixture fixture) : IClassFixture<MigratedDatabaseFixture>
{
    private const string Ts = "timestamptz";

    // Biçim: kolon:tip, sonunda ? varsa NULL olabilir.
    private static readonly Dictionary<string, string[]> ExpectedTables = new()
    {
        ["users"] = ["id:int", "username:varchar(32)", "full_name:varchar(100)", "email:varchar(150)?", "role:smallint",
            "password_hash:varchar(100)?", "must_change_password:boolean", "is_active:boolean", $"verified_at:{Ts}?",
            "external_ref:varchar(50)?", $"created_at:{Ts}"],
        ["classes"] = ["id:int", "name:varchar(100)", "term:varchar(20)", "instructor_id:int", $"created_at:{Ts}"],
        ["class_members"] = ["class_id:int", "student_id:int", $"added_at:{Ts}"],
        ["categories"] = ["id:int", "name:varchar(100)", "parent_id:int?"],
        ["questions"] = ["id:int", "category_id:int", "type:smallint", "title:varchar(200)", "text:text",
            "default_points:numeric(6,2)", "version_no:int", "previous_version_id:int?", "is_current:boolean",
            "is_locked:boolean", "is_deleted:boolean", "created_by:int", $"created_at:{Ts}", $"updated_at:{Ts}"],
        ["question_options"] = ["id:int", "question_id:int", "order_no:smallint", "text:varchar(500)", "is_correct:boolean"],
        ["classic_answer_keys"] = ["id:int", "question_id:int", "accepted_text:varchar(500)", "normalized_text:varchar(500)"],
        ["exams"] = ["id:int", "title:varchar(200)", "description:text?", "kind:smallint", "class_id:int",
            "duration_minutes:int?", $"starts_at:{Ts}?", $"ends_at:{Ts}?", "status:smallint", $"published_at:{Ts}?",
            $"completed_at:{Ts}?", $"results_published_at:{Ts}?", "created_by:int", $"created_at:{Ts}", $"updated_at:{Ts}"],
        ["exam_questions"] = ["id:int", "exam_id:int", "question_id:int", "order_no:smallint", "points:numeric(6,2)"],
        ["submissions"] = ["id:bigint", "exam_id:int", "student_id:int", "status:smallint", $"started_at:{Ts}",
            $"last_saved_at:{Ts}?", $"submitted_at:{Ts}?", "submit_count:int", "total_score:numeric(7,2)?",
            $"graded_at:{Ts}?", "client_ip:inet?"],
        ["answers"] = ["id:bigint", "submission_id:bigint", "exam_question_id:int", "selected_option_id:int?",
            "text_answer:varchar(2000)?", "score:numeric(6,2)?", "graded_by:smallint?", "graded_note:varchar(300)?", $"updated_at:{Ts}"],
        ["activity_logs"] = ["id:bigint", $"occurred_at:{Ts}", "event_type:varchar(40)", "user_id:int?", "exam_id:int?",
            "submission_id:bigint?", "ip:inet?", "details:jsonb?"],
    };

    [Fact]
    public async Task Schema_HasExactlyTheTwelveMidtermTables()
    {
        var tables = await ReadStringsAsync(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = @schema AND table_name NOT LIKE '\\_\\_EF%'");

        Assert.Equal(ExpectedTables.Keys.Order(), tables.Order());
    }

    [Theory]
    [InlineData("users")]
    [InlineData("classes")]
    [InlineData("class_members")]
    [InlineData("categories")]
    [InlineData("questions")]
    [InlineData("question_options")]
    [InlineData("classic_answer_keys")]
    [InlineData("exams")]
    [InlineData("exam_questions")]
    [InlineData("submissions")]
    [InlineData("answers")]
    [InlineData("activity_logs")]
    public async Task Table_ColumnsMatchDesign(string table)
    {
        var columns = await ReadStringsAsync(
            """
            SELECT column_name || ':' ||
                   CASE data_type
                       WHEN 'integer' THEN 'int'
                       WHEN 'character varying' THEN 'varchar(' || character_maximum_length || ')'
                       WHEN 'numeric' THEN 'numeric(' || numeric_precision || ',' || numeric_scale || ')'
                       WHEN 'timestamp with time zone' THEN 'timestamptz'
                       ELSE data_type
                   END ||
                   CASE is_nullable WHEN 'YES' THEN '?' ELSE '' END
            FROM information_schema.columns
            WHERE table_schema = @schema AND table_name = @table
            """,
            ("table", table));

        Assert.Equal(ExpectedTables[table].Order(), columns.Order());
    }

    [Theory]
    [InlineData("users", "id")]
    [InlineData("classes", "id")]
    [InlineData("categories", "id")]
    [InlineData("questions", "id")]
    [InlineData("question_options", "id")]
    [InlineData("classic_answer_keys", "id")]
    [InlineData("exams", "id")]
    [InlineData("exam_questions", "id")]
    [InlineData("submissions", "id")]
    [InlineData("answers", "id")]
    [InlineData("activity_logs", "id")]
    public async Task PrimaryKey_IsGeneratedAlwaysAsIdentity(string table, string column)
    {
        var generation = await ReadStringsAsync(
            "SELECT identity_generation FROM information_schema.columns WHERE table_schema = @schema AND table_name = @table AND column_name = @column",
            ("table", table), ("column", column));

        Assert.Equal("ALWAYS", Assert.Single(generation));
    }

    [Theory]
    [InlineData("ix_users_username", "UNIQUE", "(username)")]
    [InlineData("ix_users_email_lower", "UNIQUE", "lower(")]
    [InlineData("ix_users_role", "", "(role)")]
    [InlineData("ix_classes_name_term", "UNIQUE", "(name, term)")]
    [InlineData("ix_class_members_student_id", "", "(student_id)")]
    [InlineData("ix_categories_parent_id_name", "UNIQUE", "NULLS NOT DISTINCT")]
    [InlineData("ix_questions_category_id_current", "", "WHERE")]
    [InlineData("ix_questions_type", "", "(type)")]
    [InlineData("ix_questions_previous_version_id", "UNIQUE", "(previous_version_id)")]
    [InlineData("ix_question_options_question_id_order_no", "UNIQUE", "(question_id, order_no)")]
    [InlineData("ix_question_options_single_correct", "UNIQUE", "WHERE is_correct")]
    [InlineData("ix_classic_answer_keys_question_id_normalized_text", "UNIQUE", "(question_id, normalized_text)")]
    [InlineData("ix_exams_class_id_status", "", "(class_id, status)")]
    [InlineData("ix_exams_status_ends_at", "", "(status, ends_at)")]
    [InlineData("ix_exam_questions_exam_id_question_id", "UNIQUE", "(exam_id, question_id)")]
    [InlineData("ix_exam_questions_exam_id_order_no", "UNIQUE", "(exam_id, order_no)")]
    [InlineData("ix_submissions_exam_id_student_id", "UNIQUE", "(exam_id, student_id)")]
    [InlineData("ix_submissions_exam_id_status", "", "(exam_id, status)")]
    [InlineData("ix_answers_submission_id_exam_question_id", "UNIQUE", "(submission_id, exam_question_id)")]
    [InlineData("ix_activity_logs_exam_id_occurred_at", "", "(exam_id, occurred_at)")]
    [InlineData("ix_activity_logs_user_id_occurred_at", "", "(user_id, occurred_at)")]
    [InlineData("ix_activity_logs_event_type", "", "(event_type)")]
    public async Task Index_ExistsAsDesigned(string name, string uniqueness, string definitionPart)
    {
        var definitions = await ReadStringsAsync(
            "SELECT indexdef FROM pg_indexes WHERE schemaname = @schema AND indexname = @name", ("name", name));

        var definition = Assert.Single(definitions);
        Assert.Contains(definitionPart, definition);
        Assert.Equal(uniqueness == "UNIQUE", definition.StartsWith("CREATE UNIQUE"));
    }

    [Theory]
    [InlineData("class_members", "class_id", "CASCADE")]
    [InlineData("class_members", "student_id", "RESTRICT")]
    [InlineData("question_options", "question_id", "CASCADE")]
    [InlineData("classic_answer_keys", "question_id", "CASCADE")]
    [InlineData("exam_questions", "exam_id", "CASCADE")]
    [InlineData("exam_questions", "question_id", "RESTRICT")]
    [InlineData("answers", "submission_id", "CASCADE")]
    [InlineData("answers", "exam_question_id", "RESTRICT")]
    [InlineData("exams", "class_id", "RESTRICT")]
    public async Task ForeignKey_HasDesignedDeleteRule(string table, string column, string expectedRule)
    {
        var rules = await ReadStringsAsync(
            """
            SELECT rc.delete_rule
            FROM information_schema.referential_constraints rc
            JOIN information_schema.key_column_usage k
              ON k.constraint_name = rc.constraint_name AND k.constraint_schema = rc.constraint_schema
            WHERE rc.constraint_schema = @schema AND k.table_name = @table AND k.column_name = @column
            """,
            ("table", table), ("column", column));

        Assert.Equal(expectedRule, Assert.Single(rules));
    }

    private async Task<List<string>> ReadStringsAsync(string sql, params (string Name, string Value)[] parameters)
    {
        await using var db = fixture.CreateDbContext();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("schema", fixture.Schema);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }
}
