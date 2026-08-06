namespace QLHV.Tests.Assignments;

public sealed class LookupFieldContractTests
{
    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Shared_lookup_owns_debounce_cancellation_stale_response_and_accessible_keyboard_contract()
    {
        var lookup = AssignmentSourceTestHelper.Read("client", "src", "components", "SearchLookup.tsx");

        Assert.Contains("debounceMs = 200", lookup, StringComparison.Ordinal);
        Assert.Contains("new AbortController()", lookup, StringComparison.Ordinal);
        Assert.Contains("controller.abort()", lookup, StringComparison.Ordinal);
        Assert.Contains("sequence !== requestSequence.current", lookup, StringComparison.Ordinal);
        Assert.Contains("result.slice(0, maxResults)", lookup, StringComparison.Ordinal);
        Assert.Contains("maxResults = 20", lookup, StringComparison.Ordinal);
        Assert.Contains("event.key === 'ArrowDown'", lookup, StringComparison.Ordinal);
        Assert.Contains("event.key === 'ArrowUp'", lookup, StringComparison.Ordinal);
        Assert.Contains("event.key === 'Enter'", lookup, StringComparison.Ordinal);
        Assert.Contains("event.key === 'Escape'", lookup, StringComparison.Ordinal);
        Assert.Contains("event.preventDefault()", lookup, StringComparison.Ordinal);
        Assert.Contains("role=\"combobox\"", lookup, StringComparison.Ordinal);
        Assert.Contains("role=\"listbox\"", lookup, StringComparison.Ordinal);
        Assert.Contains("role=\"option\"", lookup, StringComparison.Ordinal);
        Assert.Contains("aria-activedescendant", lookup, StringComparison.Ordinal);
        Assert.Contains("aria-selected={selected}", lookup, StringComparison.Ordinal);
        Assert.Contains("document.addEventListener('pointerdown'", lookup, StringComparison.Ordinal);
        Assert.Contains("createPortal", lookup, StringComparison.Ordinal);
        Assert.Contains("onChange(null)", lookup, StringComparison.Ordinal);
        Assert.Contains("disabled={disabled}", lookup, StringComparison.Ordinal);
        Assert.Contains("readOnly={readOnly}", lookup, StringComparison.Ordinal);
        Assert.DoesNotContain("options.length === 1", lookup, StringComparison.Ordinal);
        Assert.DoesNotContain("choose(options[0])", lookup, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Student_course_class_teacher_vehicle_group_and_related_fields_use_one_shared_component()
    {
        var hocVien = AssignmentSourceTestHelper.Read("client", "src", "features", "hoc-vien", "HocVienPage.tsx");
        var print = AssignmentSourceTestHelper.Read("client", "src", "features", "hoc-vien", "HocVienCardPrintPage.tsx");
        var courses = AssignmentSourceTestHelper.Read("client", "src", "features", "course-assignment", "CourseListPage.tsx");
        var detail = AssignmentSourceTestHelper.Read("client", "src", "features", "course-assignment", "CourseDetailPage.tsx");
        var assignment = AssignmentSourceTestHelper.Read("client", "src", "features", "course-assignment", "dialogs", "AssignmentCommandDialog.tsx");
        var groups = AssignmentSourceTestHelper.Read("client", "src", "features", "course-assignment", "panels", "CourseGroupsPanel.tsx");

        foreach (var source in new[] { hocVien, print, courses, detail, assignment, groups })
        {
            Assert.Contains("SearchLookup", source, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("autocomplete-list", hocVien, StringComparison.Ordinal);
        Assert.DoesNotContain("autocomplete-list", print, StringComparison.Ordinal);
        Assert.DoesNotContain("setShowKhoaSuggestions", hocVien, StringComparison.Ordinal);
        Assert.DoesNotContain("setShowHangHocSuggestions", hocVien, StringComparison.Ordinal);

        Assert.Contains("label=\"Học viên\"", detail, StringComparison.Ordinal);
        Assert.Contains("label=\"Nhóm đào tạo\"", detail, StringComparison.Ordinal);
        Assert.Contains("label=\"Khóa\"", courses, StringComparison.Ordinal);
        Assert.Contains("label=\"Hạng học\"", courses, StringComparison.Ordinal);
        Assert.Contains("label={`Tra cứu ${label}`}", assignment, StringComparison.Ordinal);
        Assert.Contains("function ReferenceSelect", groups, StringComparison.Ordinal);
        Assert.Contains("<SearchLookup", AssignmentSourceTestHelper.Section(groups, "function ReferenceSelect", "function InactivateGroupDialog"), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Lookup_selection_stores_stable_keys_and_searches_server_or_bounded_course_references()
    {
        var courses = AssignmentSourceTestHelper.Read("client", "src", "features", "course-assignment", "CourseListPage.tsx");
        var detail = AssignmentSourceTestHelper.Read("client", "src", "features", "course-assignment", "CourseDetailPage.tsx");
        var assignment = AssignmentSourceTestHelper.Read("client", "src", "features", "course-assignment", "dialogs", "AssignmentCommandDialog.tsx");
        var groups = AssignmentSourceTestHelper.Read("client", "src", "features", "course-assignment", "panels", "CourseGroupsPanel.tsx");
        var lookup = AssignmentSourceTestHelper.Read("client", "src", "components", "SearchLookup.tsx");

        Assert.Contains("getHocVienKhoaLookups(keyword, 20", courses, StringComparison.Ordinal);
        Assert.Contains("getHocVienHangHocLookups(keyword, 20", courses, StringComparison.Ordinal);
        Assert.Contains("maKhoa: option?.maKhoa", courses, StringComparison.Ordinal);
        Assert.Contains("hangDaoTao: option?.maHangDT", courses, StringComparison.Ordinal);
        Assert.Contains("searchHocVien({", detail, StringComparison.Ordinal);
        Assert.Contains("pageSize: 20", detail, StringComparison.Ordinal);
        Assert.Contains("getKey={(option) => option.hocVienId}", detail, StringComparison.Ordinal);
        Assert.Contains("getKey={(option) => option.groupId}", detail, StringComparison.Ordinal);
        Assert.Contains("id: reference?.id ?? null", assignment, StringComparison.Ordinal);
        Assert.Contains("onChange={(item) => onChange(item?.id ?? null)}", groups, StringComparison.Ordinal);
        Assert.Contains("normalize('NFD')", lookup, StringComparison.Ordinal);
        Assert.Contains("toLocaleUpperCase('vi-VN')", lookup, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Lookup_dropdown_is_portaled_above_clipping_containers_and_retains_focus_styling()
    {
        var css = AssignmentSourceTestHelper.Read("client", "src", "styles", "layout.css");

        Assert.Contains(".lookup-menu-portal", css, StringComparison.Ordinal);
        Assert.Contains("position: fixed", css, StringComparison.Ordinal);
        Assert.Contains("z-index: 10000", css, StringComparison.Ordinal);
        Assert.Contains(".autocomplete-option.is-active", css, StringComparison.Ordinal);
        Assert.Contains(".autocomplete-option.is-selected", css, StringComparison.Ordinal);
        Assert.Contains(".lookup-clear", css, StringComparison.Ordinal);
    }
}
