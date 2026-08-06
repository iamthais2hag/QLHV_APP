using QLHV.Application.Assignments;

namespace QLHV.Tests.Assignments;

public sealed class AssignmentApiUiContractTests
{
    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Backend_registers_nine_distinct_assignment_capabilities()
    {
        var expected = new[]
        {
            AssignmentPolicies.ViewCatalogs,
            AssignmentPolicies.ManageDossierReceivers,
            AssignmentPolicies.ManageGroups,
            AssignmentPolicies.AssignSingle,
            AssignmentPolicies.AssignBulk,
            AssignmentPolicies.ImportPreview,
            AssignmentPolicies.ImportConfirm,
            AssignmentPolicies.Export,
            AssignmentPolicies.ViewHistory,
        };
        Assert.Equal(9, expected.Distinct(StringComparer.Ordinal).Count());

        var program = AssignmentSourceTestHelper.Read("server", "QLHV.Api", "Program.cs");
        foreach (var policy in expected)
        {
            var fieldName = typeof(AssignmentPolicies).GetFields()
                .Single(field => Equals(field.GetValue(null), policy)).Name;
            Assert.Contains($"options.AddPolicy(AssignmentPolicies.{fieldName}", program, StringComparison.Ordinal);
        }
        Assert.Contains("AssignmentPolicies.ImportConfirm", program, StringComparison.Ordinal);
        Assert.Contains("RequireRole(AppRoles.Admin)", program, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Source_teacher_and_vehicle_api_surfaces_are_read_only()
    {
        var controllers = AssignmentSourceTestHelper.Read(
            "server", "QLHV.Api", "Controllers", "AssignmentControllers.cs");
        var teacher = AssignmentSourceTestHelper.Section(
            controllers,
            "public sealed class GiaoVienController",
            "public sealed class GiaoVienHoSoController");
        var vehicle = AssignmentSourceTestHelper.Section(
            controllers,
            "public sealed class XeTapLaiController",
            "public sealed class KhoaHocController");

        foreach (var section in new[] { teacher, vehicle })
        {
            Assert.Contains("[HttpGet]", section, StringComparison.Ordinal);
            Assert.DoesNotContain("[HttpPost", section, StringComparison.Ordinal);
            Assert.DoesNotContain("[HttpPut", section, StringComparison.Ordinal);
            Assert.DoesNotContain("[HttpDelete", section, StringComparison.Ordinal);
        }
        Assert.Contains("[Authorize(Policy=AssignmentPolicies.ViewCatalogs)]", controllers, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Routes_cover_catalog_group_assignment_excel_and_history_workflows()
    {
        var source = AssignmentSourceTestHelper.Read(
            "server", "QLHV.Api", "Controllers", "AssignmentControllers.cs");

        foreach (var route in new[]
                 {
                     "api/giao-vien",
                     "api/giao-vien-ho-so",
                     "api/xe-tap-lai",
                     "api/khoa-hoc",
                     "api/nhom-dao-tao",
                     "api/phan-cong",
                     "{id:long}/chi-tiet-phan-cong",
                     "{id:long}/nhom-dao-tao",
                     "{id:long}/phan-cong/export",
                     "{id:long}/phan-cong/import/template",
                     "{id:long}/phan-cong/import/preview",
                     "{id:long}/phan-cong/import/confirm",
                     "{id:long}/phan-cong/history",
                 })
        {
            Assert.Contains(route, source, StringComparison.Ordinal);
        }

        Assert.Contains("AssignmentPolicies.AssignBulk", source, StringComparison.Ordinal);
        Assert.Contains("RequestSizeLimit(AssignmentRules.MaxImportBytes", source, StringComparison.Ordinal);
        Assert.Contains("IdempotencyKey", AssignmentSourceTestHelper.Read(
            "server", "QLHV.Application", "Assignments", "AssignmentDtos.cs"), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Client_registers_real_teacher_vehicle_course_and_course_detail_pages()
    {
        var app = Client("App.tsx");
        var menu = Client("navigation", "menu.ts");

        Assert.Contains("<TeacherPage />", app, StringComparison.Ordinal);
        Assert.Contains("<VehiclePage />", app, StringComparison.Ordinal);
        Assert.Contains("<CourseListPage />", app, StringComparison.Ordinal);
        Assert.Contains("<CourseDetailPage />", app, StringComparison.Ordinal);
        Assert.Contains("path=\"/khoa-hoc/:khoaHocId\"", app, StringComparison.Ordinal);
        foreach (var route in new[] { "'/giao-vien'", "'/xe-tap-lai'", "'/khoa-hoc'" })
        {
            Assert.Contains(route, menu, StringComparison.Ordinal);
        }
        Assert.Contains("CanViewAssignmentCatalogs", menu, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Client_uses_exact_csdt_profile_values_and_never_short_aliases()
    {
        var types = Feature("types.ts");
        var filters = string.Join('\n',
            Feature("TeacherPage.tsx"),
            Feature("VehiclePage.tsx"),
            Feature("CourseListPage.tsx"));

        Assert.Contains("'CSDT_OTO' | 'CSDT_MOTO'", types, StringComparison.Ordinal);
        Assert.Contains("value=\"CSDT_OTO\"", filters, StringComparison.Ordinal);
        Assert.Contains("value=\"CSDT_MOTO\"", filters, StringComparison.Ordinal);
        Assert.DoesNotContain("value=\"OTO\"", filters, StringComparison.Ordinal);
        Assert.DoesNotContain("value=\"MOTO\"", filters, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Teacher_and_vehicle_pages_mark_source_catalogs_read_only_and_expose_usage_state()
    {
        var teacher = Feature("TeacherPage.tsx");
        var vehicle = Feature("VehiclePage.tsx");
        var api = Feature("api.ts");

        Assert.Contains("searchSourceTeachers", teacher, StringComparison.Ordinal);
        Assert.DoesNotContain("searchDossierReceivers", teacher, StringComparison.Ordinal);
        Assert.Contains("Chờ bằng chứng quan hệ nguồn", teacher, StringComparison.Ordinal);
        Assert.Contains("không cho thêm, sửa hoặc nhập tay", teacher, StringComparison.Ordinal);
        Assert.Contains("courseUsageCount", teacher, StringComparison.Ordinal);
        Assert.Contains("studentUsageCount", teacher, StringComparison.Ordinal);
        Assert.Contains("assignment-readonly-chip", teacher, StringComparison.Ordinal);
        Assert.Contains("SourceTeacherDetailDialog", teacher, StringComparison.Ordinal);
        Assert.Contains("Mở chi tiết", teacher, StringComparison.Ordinal);

        Assert.Contains("searchVehicles", vehicle, StringComparison.Ordinal);
        Assert.Contains("courseUsageCount", vehicle, StringComparison.Ordinal);
        Assert.Contains("groupUsageCount", vehicle, StringComparison.Ordinal);
        Assert.Contains("studentUsageCount", vehicle, StringComparison.Ordinal);
        Assert.Contains("sourceProfileCode", vehicle, StringComparison.Ordinal);
        Assert.Contains("isManualReview", vehicle, StringComparison.Ordinal);
        Assert.Contains("assignment-readonly-chip", vehicle, StringComparison.Ordinal);

        Assert.DoesNotContain("createSourceTeacher", api, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("updateSourceTeacher", api, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("createVehicle", api, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("updateVehicle", api, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Course_ui_has_seven_sections_bulk_selection_overrides_and_concurrency_feedback()
    {
        var page = Feature("CourseDetailPage.tsx");
        var dialog = Feature("dialogs", "AssignmentCommandDialog.tsx");
        var groups = Feature("panels", "CourseGroupsPanel.tsx");
        var history = Feature("panels", "CourseHistoryPanel.tsx");

        foreach (var key in new[] { "information", "students", "groups", "resources", "excel", "history", "completion" })
        {
            Assert.Contains($"key: '{key}'", page, StringComparison.Ordinal);
        }
        Assert.Contains("allFiltered", page, StringComparison.Ordinal);
        Assert.Contains("unassignedOnly", page, StringComparison.Ordinal);
        Assert.Contains("expectedRowVersions", page, StringComparison.Ordinal);
        Assert.Contains("STUDENT_OVERRIDE", page, StringComparison.Ordinal);
        Assert.Contains("previewAssignment", dialog, StringComparison.Ordinal);
        Assert.Contains("confirmAssignment", dialog, StringComparison.Ordinal);
        Assert.Contains("createIdempotencyKey", dialog, StringComparison.Ordinal);
        Assert.Contains("preview.conflictCount > 0", dialog, StringComparison.Ordinal);
        Assert.Contains("preview.invalidCount > 0", dialog, StringComparison.Ordinal);
        foreach (var mode in new[] { "UNOVERRIDDEN_ONLY", "REPLACE_ALL", "NO_CURRENT_CHANGE" })
        {
            Assert.Contains(mode, groups, StringComparison.Ordinal);
        }
        Assert.Contains("Full-snapshot history", history, StringComparison.Ordinal);
        Assert.Contains("NO_CHANGE", history, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Excel_ui_is_course_scoped_all_or_nothing_and_renders_bounded_preview()
    {
        var panel = Feature("panels", "AssignmentExcelPanel.tsx");

        Assert.Contains("course.khoaHocId", panel, StringComparison.Ordinal);
        Assert.Contains("course.sourceProfileCode", panel, StringComparison.Ordinal);
        Assert.Contains("previewAssignmentImport", panel, StringComparison.Ordinal);
        Assert.Contains("confirmAssignmentImport", panel, StringComparison.Ordinal);
        Assert.Contains("createIdempotencyKey", panel, StringComparison.Ordinal);
        foreach (var status in new[]
                 {
                     "ready", "noChange", "notFound", "ambiguous",
                     "inactiveReference", "invalid", "conflict",
                 })
        {
            Assert.Contains($"preview.counts.{status}", panel, StringComparison.Ordinal);
        }
        Assert.Contains("MAX_RENDERED_PREVIEW_ROWS", panel, StringComparison.Ordinal);
        Assert.Contains("preview.rows.slice(0, MAX_RENDERED_PREVIEW_ROWS)", panel, StringComparison.Ordinal);
        Assert.Contains("blockingCount", panel, StringComparison.Ordinal);
        Assert.Contains("preview.counts.ready === 0", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("Date.now()", panel, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "new Date(preview.expiresAtUtc).getTime()",
            panel,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Client_clock_is_display_diagnostic_only_and_never_authorizes_confirm()
    {
        var ui = Feature("ui.tsx");
        var panel = Feature("panels", "AssignmentExcelPanel.tsx");
        var dialog = Feature("dialogs", "AssignmentCommandDialog.tsx");
        var runtime = Client(
            "features", "runtime-status", "RuntimeStatusPage.tsx");

        Assert.Contains("Asia/Ho_Chi_Minh", ui, StringComparison.Ordinal);
        Assert.Contains("crypto.getRandomValues", ui, StringComparison.Ordinal);
        Assert.DoesNotContain("Date.now()", ui, StringComparison.Ordinal);
        Assert.DoesNotContain("Date.now()", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("Date.now()", dialog, StringComparison.Ordinal);
        Assert.Contains(
            "Giờ máy người dùng không được dùng làm thời điểm ghi nhận hệ thống.",
            runtime,
            StringComparison.Ordinal);
        Assert.Contains("Lệch trình duyệt/máy chủ", runtime, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Client_permissions_mirror_backend_capabilities_and_keep_import_confirm_admin_only()
    {
        var permissions = Client("features", "auth", "permissions.ts");
        foreach (var permission in new[]
                 {
                     "CanViewAssignmentCatalogs",
                     "CanManageDossierReceivers",
                     "CanManageCourseGroups",
                     "CanAssignStudents",
                     "CanBulkAssignStudents",
                     "CanPreviewAssignmentImport",
                     "CanConfirmAssignmentImport",
                     "CanExportAssignments",
                     "CanViewAssignmentHistory",
                 })
        {
            Assert.Contains($"'{permission}'", permissions, StringComparison.Ordinal);
        }

        var employeeBlock = AssignmentSourceTestHelper.Section(
            permissions,
            "if (role === 'Employee')",
            "return permission === 'CanViewBusinessData'");
        Assert.DoesNotContain("CanConfirmAssignmentImport", employeeBlock, StringComparison.Ordinal);
    }

    private static string Feature(params string[] path) => AssignmentSourceTestHelper.Read(
        ["client", "src", "features", "course-assignment", .. path]);

    private static string Client(params string[] path) => AssignmentSourceTestHelper.Read(
        ["client", "src", .. path]);
}
