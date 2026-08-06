import {
  useCallback,
  useEffect,
  useState,
  type FormEvent,
} from 'react';
import { Link } from 'react-router-dom';
import { useDataVersionRefresh } from '../data-version/useDataVersionRefresh';
import SearchLookup from '../../components/SearchLookup';
import { getHocVienHangHocLookups, getHocVienKhoaLookups } from '../hoc-vien/api';
import type { HocVienHangHocLookup, HocVienKhoaLookup } from '../hoc-vien/types';
import { searchCourses } from './api';
import type {
  CourseListQuery,
  KhoaHocListItem,
  PagedResult,
} from './types';
import {
  EmptyState,
  formatDate,
  PageMessage,
  Pager,
  StatusBadge,
} from './ui';

const PAGE_SIZE = 25;

interface CourseFilters {
  maKhoa: string;
  tenKhoa: string;
  hangDaoTao: string;
  loaiDaoTao: string;
  trangThai: string;
  sourceProfileCode: string;
  tuNgay: string;
  denNgay: string;
}

const EMPTY_FILTERS: CourseFilters = {
  maKhoa: '',
  tenKhoa: '',
  hangDaoTao: '',
  loaiDaoTao: '',
  trangThai: '',
  sourceProfileCode: '',
  tuNgay: '',
  denNgay: '',
};

export default function CourseListPage() {
  const [draft, setDraft] = useState<CourseFilters>(EMPTY_FILTERS);
  const [filters, setFilters] = useState<CourseFilters>(EMPTY_FILTERS);
  const [courseLookupInput, setCourseLookupInput] = useState('');
  const [selectedCourse, setSelectedCourse] = useState<HocVienKhoaLookup | null>(null);
  const [classLookupInput, setClassLookupInput] = useState('');
  const [selectedClass, setSelectedClass] = useState<HocVienHangHocLookup | null>(null);
  const [lookupValidation, setLookupValidation] = useState<string | null>(null);
  const [page, setPage] = useState(1);
  const [result, setResult] = useState<PagedResult<KhoaHocListItem> | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async (signal?: AbortSignal) => {
    setLoading(true);
    setError(null);
    const query: CourseListQuery = {
      ...filters,
      page,
      pageSize: PAGE_SIZE,
    };
    try {
      setResult(await searchCourses(query, signal));
    } catch (loadError) {
      if (!(loadError instanceof DOMException && loadError.name === 'AbortError')) {
        setError(loadError instanceof Error ? loadError.message : 'Không thể tải danh sách khóa học.');
      }
    } finally {
      if (!signal?.aborted) setLoading(false);
    }
  }, [filters, page]);

  useEffect(() => {
    const controller = new AbortController();
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const versionRefresh = useDataVersionRefresh({
    resources: ['khoaHocVersion'],
    onVersionChanged: async () => load(),
  });

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (courseLookupInput.trim() && !selectedCourse) {
      setLookupValidation('Vui lòng chọn Khóa trong danh sách kết quả.');
      return;
    }
    if (classLookupInput.trim() && !selectedClass) {
      setLookupValidation('Vui lòng chọn Hạng học trong danh sách kết quả.');
      return;
    }
    setLookupValidation(null);
    setPage(1);
    setFilters(trimFilters(draft));
  }

  function reset() {
    setDraft(EMPTY_FILTERS);
    setFilters(EMPTY_FILTERS);
    setCourseLookupInput('');
    setSelectedCourse(null);
    setClassLookupInput('');
    setSelectedClass(null);
    setLookupValidation(null);
    setPage(1);
  }

  return (
    <div className="assignment-page">
      <section className="panel assignment-hero">
        <div>
          <span className="assignment-eyebrow">Khóa học cơ sở đào tạo</span>
          <h2>Khóa học và phân công</h2>
          <p>
            Chọn đúng một khóa để quản lý nhóm, giáo viên, xe và phân công học viên.
            Thông tin khóa là dữ liệu nguồn chỉ đọc.
          </p>
        </div>
        <span className="assignment-readonly-chip">App_KhoaHoc · chỉ đọc nguồn</span>
      </section>

      <form className="toolbar assignment-course-filters" onSubmit={submit}>
        <div className="toolbar__row">
          <SearchLookup
            id="assignment-course-lookup"
            label="Khóa"
            value={selectedCourse}
            inputValue={courseLookupInput}
            onInputValueChange={(value) => {
              setCourseLookupInput(value);
              setLookupValidation(null);
              setDraft((current) => ({ ...current, maKhoa: '', tenKhoa: '' }));
            }}
            onChange={(option) => {
              setSelectedCourse(option);
              setLookupValidation(null);
              setDraft((current) => ({ ...current, maKhoa: option?.maKhoa ?? '', tenKhoa: '' }));
            }}
            loadOptions={(keyword, signal) => getHocVienKhoaLookups(keyword, 20, selectedClass?.maHangDT, signal)}
            getKey={(option) => option.maKhoa}
            getLabel={(option) => option.label}
            getDescription={(option) => option.tenKhoa ? `Mã nguồn: ${option.maKhoa}` : null}
            placeholder="Mã khóa, tên khóa hoặc mã nguồn"
            emptyText="Không có khóa phù hợp"
            errorText="Không tải được danh sách Khóa."
          />
          <SearchLookup
            id="assignment-class-lookup"
            label="Hạng học"
            value={selectedClass}
            inputValue={classLookupInput}
            onInputValueChange={(value) => {
              setClassLookupInput(value);
              setLookupValidation(null);
              setDraft((current) => ({ ...current, hangDaoTao: '' }));
            }}
            onChange={(option) => {
              setSelectedClass(option);
              setLookupValidation(null);
              setSelectedCourse(null);
              setCourseLookupInput('');
              setDraft((current) => ({ ...current, maKhoa: '', tenKhoa: '', hangDaoTao: option?.maHangDT ?? '' }));
            }}
            loadOptions={(keyword, signal) => getHocVienHangHocLookups(keyword, 20, signal)}
            getKey={(option) => option.maHangDT}
            getLabel={(option) => option.label}
            getDescription={(option) => option.hangGplxHoc ? `Mã hạng: ${option.maHangDT}` : null}
            placeholder="Mã hạng hoặc tên hạng"
            emptyText="Không có hạng học phù hợp"
            errorText="Không tải được danh sách Hạng học."
          />
          <label className="field">
            <span className="field__label">Loại / hình thức đào tạo</span>
            <input
              className="field__input"
              value={draft.loaiDaoTao}
              onChange={(event) => setDraft({ ...draft, loaiDaoTao: event.target.value })}
              maxLength={100}
            />
          </label>
        </div>
        <div className="toolbar__row assignment-toolbar-row">
          <label className="field">
            <span className="field__label">Nguồn</span>
            <select
              className="field__input"
              value={draft.sourceProfileCode}
              onChange={(event) => setDraft({ ...draft, sourceProfileCode: event.target.value })}
            >
              <option value="">Tất cả OTO/MOTO</option>
              <option value="CSDT_OTO">OTO</option>
              <option value="CSDT_MOTO">MOTO</option>
            </select>
          </label>
          <label className="field">
            <span className="field__label">Trạng thái</span>
            <select
              className="field__input"
              value={draft.trangThai}
              onChange={(event) => setDraft({ ...draft, trangThai: event.target.value })}
            >
              <option value="">Tất cả trạng thái</option>
              <option value="ACTIVE">Đang đào tạo</option>
              <option value="INACTIVE">Đã kết thúc/ngừng dùng</option>
              <option value="MANUAL_REVIEW">Cần kiểm tra</option>
            </select>
          </label>
          <label className="field">
            <span className="field__label">Khai giảng từ ngày</span>
            <input
              type="date"
              className="field__input"
              value={draft.tuNgay}
              onChange={(event) => setDraft({ ...draft, tuNgay: event.target.value })}
            />
          </label>
          <label className="field">
            <span className="field__label">Đến ngày</span>
            <input
              type="date"
              className="field__input"
              value={draft.denNgay}
              onChange={(event) => setDraft({ ...draft, denNgay: event.target.value })}
            />
          </label>
          <div className="toolbar__actions">
            <button type="submit" className="btn btn--primary" disabled={loading}>Tìm kiếm</button>
            <button type="button" className="btn btn--ghost" onClick={reset} disabled={loading}>Xóa lọc</button>
            <button type="button" className="btn btn--ghost" onClick={() => void load()} disabled={loading}>
              {loading ? 'Đang tải...' : 'Tải lại'}
            </button>
          </div>
        </div>
      </form>

      {lookupValidation && <PageMessage kind="warning">{lookupValidation}</PageMessage>}
      {error && <PageMessage kind="error">{error}</PageMessage>}
      {versionRefresh.error && <PageMessage kind="warning">{versionRefresh.error}</PageMessage>}
      {loading && !result && <div className="panel"><EmptyState>Đang tải danh sách khóa học...</EmptyState></div>}
      {result && result.items.length === 0 && <div className="panel"><EmptyState>Không có khóa học phù hợp.</EmptyState></div>}
      {!!result?.items.length && (
        <>
          <div className="table-wrap">
            <table className="table assignment-table assignment-table--course">
              <thead>
                <tr>
                  <th>Nguồn</th>
                  <th>Mã khóa</th>
                  <th>Tên khóa</th>
                  <th>Hạng / loại đào tạo</th>
                  <th>Khai giảng</th>
                  <th>Bế giảng</th>
                  <th>Số quyết định</th>
                  <th>Học viên</th>
                  <th>Chưa phân công</th>
                  <th>Trạng thái</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {result.items.map((course) => (
                  <tr key={`${course.sourceProfileCode}:${course.khoaHocId}`}>
                    <td><span className="assignment-source">{course.sourceProfileCode}</span></td>
                    <td><strong>{course.maKhoa}</strong></td>
                    <td>{course.tenKhoa || '—'}</td>
                    <td>
                      {course.hangDaoTao || '—'}
                      {course.loaiDaoTao && <small className="assignment-cell-note">{course.loaiDaoTao}</small>}
                    </td>
                    <td>{formatDate(course.ngayKhaiGiang)}</td>
                    <td>{formatDate(course.ngayBeGiang)}</td>
                    <td>{course.soQuyetDinh || '—'}</td>
                    <td>{course.learnerCount.toLocaleString('vi-VN')}</td>
                    <td>
                      <span className={course.unassignedCount > 0 ? 'assignment-count-warning' : ''}>
                        {course.unassignedCount.toLocaleString('vi-VN')}
                      </span>
                      {course.manualReviewCount > 0 && (
                        <small className="assignment-cell-note">
                          {course.manualReviewCount} cần kiểm tra
                        </small>
                      )}
                    </td>
                    <td>
                      <StatusBadge
                        active={course.isActive}
                        manualReview={course.manualReviewCount > 0}
                        label={course.trangThai}
                      />
                    </td>
                    <td>
                      <Link
                        className="btn btn--primary btn--sm"
                        to={`/khoa-hoc/${course.khoaHocId}`}
                      >
                        Mở chi tiết
                      </Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pager result={result} onPage={setPage} disabled={loading} />
        </>
      )}
    </div>
  );
}

function trimFilters(value: CourseFilters): CourseFilters {
  return {
    ...value,
    maKhoa: value.maKhoa.trim(),
    tenKhoa: value.tenKhoa.trim(),
    hangDaoTao: value.hangDaoTao.trim(),
    loaiDaoTao: value.loaiDaoTao.trim(),
  };
}
