using QLHV.Application.HocVien;
using QLHV.Application.HocVien.Dtos;
using QLHV.Application.HocVien.Printing;
using QLHV.Shared.Paging;

namespace QLHV.Tests.HocVien;

public sealed class HocVienCardPrintServiceTests
{
    [Fact]
    public async Task Print_selected_uses_selected_ids_only()
    {
        var repository = new FakeRepository();
        repository.ByIdsResult = [CreateHocVien(2), CreateHocVien(3)];
        var pdf = new FakePdfGenerator();
        var service = CreateService(repository, pdf);

        var result = await service.PrintCardsAsync(new HocVienCardPrintRequest
        {
            Mode = "selected",
            HocVienIds = [2, 3, 3],
        });

        Assert.Equal([2, 3], repository.LastByIds);
        Assert.Equal([2, 3], pdf.LastIds);
        Assert.Equal("application/pdf", result.ContentType);
    }

    [Fact]
    public async Task Print_passes_trimmed_custom_titles_to_pdf_generator()
    {
        var repository = new FakeRepository
        {
            ByIdsResult = [CreateHocVien(1)],
        };
        var pdf = new FakePdfGenerator();
        var service = CreateService(repository, pdf);

        await service.PrintCardsAsync(new HocVienCardPrintRequest
        {
            Mode = "single",
            HocVienId = 1,
            TitleLine1 = "  Cơ quan quản lý  ",
            TitleLine2 = "  Cơ sở đào tạo  ",
            Typography = new HocVienCardTypographyRequest
            {
                OrganizationLine2 = new HocVienCardTextStyleRequest
                {
                    FontFamily = "Arial",
                    FontSizePt = 11d,
                    Bold = true,
                    Uppercase = false,
                    Italic = true,
                },
            },
        });

        Assert.Equal("Cơ quan quản lý", pdf.LastTitleOptions?.TitleLine1);
        Assert.Equal("Cơ sở đào tạo", pdf.LastTitleOptions?.TitleLine2);
        Assert.Equal("Arial", pdf.LastTitleOptions?.Typography?.OrganizationLine2.FontFamily);
        Assert.Equal(11d, pdf.LastTitleOptions?.Typography?.OrganizationLine2.FontSizePt);
        Assert.True(pdf.LastTitleOptions?.Typography?.OrganizationLine2.Bold);
        Assert.Equal(
            HocVienCardTextCase.Original,
            pdf.LastTitleOptions?.Typography?.OrganizationLine2.TextCase);
        Assert.True(pdf.LastTitleOptions?.Typography?.OrganizationLine2.Italic);
    }

    [Fact]
    public async Task Print_course_uses_ma_khoa()
    {
        var repository = new FakeRepository();
        repository.ByCourseResult = [CreateHocVien(4)];
        var pdf = new FakePdfGenerator();
        var service = CreateService(repository, pdf);

        await service.PrintCardsAsync(new HocVienCardPrintRequest
        {
            Mode = "course",
            MaKhoa = " 66016K26A0001 ",
        });

        Assert.Equal("66016K26A0001", repository.LastMaKhoa);
        Assert.Equal([4], pdf.LastIds);
    }

    [Fact]
    public async Task Print_teacher_in_course_is_explicitly_blocked_until_relationship_is_available()
    {
        var service = CreateService(new FakeRepository(), new FakePdfGenerator());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PrintCardsAsync(new HocVienCardPrintRequest
            {
                Mode = "teacherInCourse",
                MaKhoa = "66016K26A0001",
                GiaoVienId = 7,
            }));

        Assert.Contains("teacherInCourse", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Print_preview_for_one_student_reports_one_page()
    {
        var repository = new FakeRepository
        {
            ByIdsResult = [CreateHocVien(1)],
        };
        var service = CreateService(repository, new FakePdfGenerator());

        var result = await service.PreviewPrintCardsAsync(new HocVienCardPrintRequest
        {
            Mode = "selected",
            HocVienIds = [1],
        });

        Assert.Equal(1, result.TotalStudents);
        Assert.Equal(1, result.TotalPages);
        Assert.Equal(12, result.CardsPerPage);
        Assert.Equal(HocVienCardTemplate.Default.OrganizationLine1, result.OrganizationLine1);
        Assert.Equal(HocVienCardTemplate.Default.OrganizationLine2, result.OrganizationLine2);
        Assert.Equal(HocVienCardTemplate.Default.ResolveCardTitle(), result.CardTitle);
    }

    [Fact]
    public async Task Print_preview_uses_uppercase_custom_titles_and_blank_title_fallback()
    {
        var repository = new FakeRepository
        {
            ByIdsResult = [CreateHocVien(1)],
        };
        var service = CreateService(repository, new FakePdfGenerator());

        var result = await service.PreviewPrintCardsAsync(new HocVienCardPrintRequest
        {
            Mode = "single",
            HocVienId = 1,
            TitleLine1 = "  Cơ quan quản lý  ",
            TitleLine2 = "   ",
        });

        Assert.Equal("CƠ QUAN QUẢN LÝ", result.OrganizationLine1);
        Assert.Equal(HocVienCardTemplate.Default.OrganizationLine2, result.OrganizationLine2);
    }

    [Fact]
    public async Task Print_preview_for_thirteen_students_reports_two_pages()
    {
        var repository = new FakeRepository
        {
            ByIdsResult = Enumerable.Range(1, 13).Select(CreateHocVien).ToArray(),
        };
        var service = CreateService(repository, new FakePdfGenerator());

        var result = await service.PreviewPrintCardsAsync(new HocVienCardPrintRequest
        {
            Mode = "selected",
            HocVienIds = Enumerable.Range(1, 13).ToArray(),
        });

        Assert.Equal(13, result.TotalStudents);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal(12, result.CardsPerPage);
        Assert.Equal("A4 ngang 3x4", result.LayoutName);
    }

    [Fact]
    public async Task Missing_photo_mode_skip_removes_missing_students_from_preview_and_pdf()
    {
        var repository = new FakeRepository
        {
            ByIdsResult = [CreateHocVien(1), CreateHocVien(2)],
        };
        var photo = new FakePhotoService();
        photo.Inspections[2] = MissingPhoto(CreateHocVien(2));
        var pdf = new FakePdfGenerator();
        var service = CreateService(repository, pdf, photo);

        var preview = await service.PreviewPrintCardsAsync(new HocVienCardPrintRequest
        {
            Mode = "selected",
            HocVienIds = [1, 2],
            MissingPhotoMode = "skip",
        });
        await service.PrintCardsAsync(new HocVienCardPrintRequest
        {
            Mode = "selected",
            HocVienIds = [1, 2],
            MissingPhotoMode = "skip",
        });

        Assert.Equal(1, preview.MissingPhotoCount);
        Assert.Equal([1], preview.Items.Select(item => item.HocVienId).ToArray());
        Assert.Equal([1], pdf.LastIds);
    }

    [Fact]
    public async Task Missing_photo_mode_placeholder_keeps_missing_students()
    {
        var repository = new FakeRepository
        {
            ByIdsResult = [CreateHocVien(1), CreateHocVien(2)],
        };
        var photo = new FakePhotoService();
        photo.Inspections[2] = MissingPhoto(CreateHocVien(2));
        var service = CreateService(repository, new FakePdfGenerator(), photo);

        var preview = await service.PreviewPrintCardsAsync(new HocVienCardPrintRequest
        {
            Mode = "selected",
            HocVienIds = [1, 2],
            MissingPhotoMode = "placeholder",
        });

        Assert.Equal(1, preview.MissingPhotoCount);
        Assert.Equal([1, 2], preview.Items.Select(item => item.HocVienId).ToArray());
    }

    [Fact]
    public async Task Print_preview_can_sort_by_ho_ten_and_ma_dk()
    {
        var repository = new FakeRepository
        {
            ByIdsResult =
            [
                new HocVienListItemDto { HocVienId = 1, MaDangKy = "B", HoVaTen = "Tran B", MaKhoa = "K" },
                new HocVienListItemDto { HocVienId = 2, MaDangKy = "A", HoVaTen = "Nguyen A", MaKhoa = "K" },
            ],
        };
        var service = CreateService(repository, new FakePdfGenerator());

        var byName = await service.PreviewPrintCardsAsync(new HocVienCardPrintRequest
        {
            Mode = "selected",
            HocVienIds = [1, 2],
            SortBy = "hoTen",
        });
        var byMaDk = await service.PreviewPrintCardsAsync(new HocVienCardPrintRequest
        {
            Mode = "selected",
            HocVienIds = [1, 2],
            SortBy = "maDK",
        });

        Assert.Equal([2, 1], byName.Items.Select(item => item.HocVienId).ToArray());
        Assert.Equal([2, 1], byMaDk.Items.Select(item => item.HocVienId).ToArray());
    }

    [Fact]
    public async Task Photo_audit_returns_expected_relative_path_without_full_path()
    {
        var repository = new FakeRepository
        {
            ExportResult = [CreateHocVien(1), CreateHocVien(2)],
        };
        var photo = new FakePhotoService();
        photo.Inspections[2] = MissingPhoto(CreateHocVien(2));
        var service = CreateService(repository, new FakePdfGenerator(), photo);

        var result = await service.AuditPhotosAsync(new HocVienPhotoAuditRequest
        {
            MaKhoa = "66016K26A0001",
            MaHangDT = "Am",
            Keyword = "Hoc",
            Page = 1,
            PageSize = 20,
        });

        Assert.Equal("66016K26A0001", repository.LastExportRequest?.MaKhoa);
        Assert.Equal("Am", repository.LastExportRequest?.MaHangDT);
        Assert.Equal("Hoc", repository.LastExportRequest?.Keyword);
        Assert.Equal(2, result.TotalItems);
        Assert.Equal(1, result.TotalHasPhoto);
        Assert.Equal(1, result.TotalMissingPhoto);
        Assert.Equal("66016K26A0001/DK1.jp2", result.Items[0].ExpectedRelativePath);
        Assert.DoesNotContain(@":\", result.Items[0].ExpectedRelativePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Photo_audit_validate_decode_flag_controls_decode_check()
    {
        var repository = new FakeRepository
        {
            ExportResult = [CreateHocVien(1)],
        };
        var photo = new FakePhotoService();
        var service = CreateService(repository, new FakePdfGenerator(), photo);

        await service.AuditPhotosAsync(new HocVienPhotoAuditRequest { ValidateDecode = false });
        await service.AuditPhotosAsync(new HocVienPhotoAuditRequest { ValidateDecode = true });

        Assert.Equal([false, true], photo.ValidateDecodeCalls);
    }

    private static HocVienService CreateService(
        FakeRepository repository,
        FakePdfGenerator pdf,
        FakePhotoService? photo = null)
        => new(repository, photo ?? new FakePhotoService(), pdf, HocVienCardTemplate.Default);

    private static HocVienListItemDto CreateHocVien(int id) => new()
    {
        HocVienId = id,
        MaDangKy = $"DK{id}",
        HoVaTen = $"Hoc vien {id}",
        MaKhoa = "66016K26A0001",
        MaHangDT = "Am",
        HangGplxHoc = "Hang Am",
    };

    private static HocVienPhotoInspectionDto MissingPhoto(HocVienListItemDto row) => new()
    {
        ExpectedRelativePath = $"{row.MaKhoa}/{row.MaDangKy}.jp2",
        HasPhoto = false,
        PhotoStatus = "Missing",
        Message = "missing",
    };

    private sealed class FakeRepository : IHocVienRepository
    {
        public IReadOnlyList<int> LastByIds { get; private set; } = [];
        public string? LastMaKhoa { get; private set; }
        public HocVienSearchRequest? LastExportRequest { get; private set; }
        public IReadOnlyList<HocVienListItemDto> ByIdsResult { get; set; } = [];
        public IReadOnlyList<HocVienListItemDto> ByCourseResult { get; set; } = [];
        public IReadOnlyList<HocVienListItemDto> ExportResult { get; set; } = [];

        public Task<PagedResult<HocVienListItemDto>> SearchAsync(
            HocVienSearchRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(PagedResult<HocVienListItemDto>.Empty(1, 20));

        public Task<HocVienListItemDto?> GetByIdAsync(
            int hocVienId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<HocVienListItemDto?>(CreateHocVien(hocVienId));

        public Task<IReadOnlyList<HocVienListItemDto>> GetByIdsAsync(
            IReadOnlyList<int> hocVienIds,
            int maxRows,
            CancellationToken cancellationToken = default)
        {
            LastByIds = hocVienIds.ToArray();
            return Task.FromResult(ByIdsResult);
        }

        public Task<IReadOnlyList<HocVienListItemDto>> GetByCourseAsync(
            string maKhoa,
            int maxRows,
            CancellationToken cancellationToken = default)
        {
            LastMaKhoa = maKhoa;
            return Task.FromResult(ByCourseResult);
        }

        public Task<IReadOnlyList<HocVienListItemDto>> ExportRowsAsync(
            HocVienSearchRequest request,
            int maxRows,
            CancellationToken cancellationToken = default)
        {
            LastExportRequest = request;
            return Task.FromResult(ExportResult);
        }

        public Task<IReadOnlyList<HocVienKhoaLookupDto>> SearchKhoaLookupsAsync(
            string? keyword,
            string? maHangDT,
            int limit,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<HocVienKhoaLookupDto>>([]);

        public Task<IReadOnlyList<HocVienHangHocLookupDto>> SearchHangHocLookupsAsync(
            string? keyword,
            int limit,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<HocVienHangHocLookupDto>>([]);
    }

    private sealed class FakePhotoService : IHocVienPhotoService
    {
        public Dictionary<int, HocVienPhotoInspectionDto> Inspections { get; } = [];
        public List<bool> ValidateDecodeCalls { get; } = [];

        public Task<HocVienPhotoPreviewDto?> GetPreviewAsync(
            HocVienListItemDto hocVien,
            CancellationToken cancellationToken = default)
            => Task.FromResult<HocVienPhotoPreviewDto?>(null);

        public Task<HocVienPhotoInspectionDto> InspectAsync(
            HocVienListItemDto hocVien,
            bool validateDecode,
            CancellationToken cancellationToken = default)
        {
            ValidateDecodeCalls.Add(validateDecode);
            return Task.FromResult(Inspections.GetValueOrDefault(hocVien.HocVienId) ?? new HocVienPhotoInspectionDto
            {
                ExpectedRelativePath = $"{hocVien.MaKhoa}/{hocVien.MaDangKy}.jp2",
                HasPhoto = true,
                PhotoStatus = "HasPhoto",
                Message = "ok",
            });
        }
    }

    private sealed class FakePdfGenerator : IHocVienCardPdfGenerator
    {
        public IReadOnlyList<int> LastIds { get; private set; } = [];

        public HocVienCardTitleOptions? LastTitleOptions { get; private set; }

        public byte[] CreatePdf(
            IReadOnlyList<HocVienListItemDto> hocViens,
            IReadOnlyDictionary<int, HocVienPhotoPreviewDto>? photosByHocVienId = null,
            HocVienCardTitleOptions? titleOptions = null)
        {
            LastIds = hocViens.Select(row => row.HocVienId).ToArray();
            LastTitleOptions = titleOptions;
            return [1, 2, 3];
        }
    }
}
