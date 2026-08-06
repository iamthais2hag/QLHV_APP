import {
  useCallback,
  useEffect,
  useMemo,
  useState,
  type FormEvent,
} from 'react';
import { Link, useParams } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { hasPermission } from '../auth/permissions';
import { useDataVersionRefresh } from '../data-version/useDataVersionRefresh';
import CourseCompletionPanel from '../course-completion/CourseCompletionPanel';
import SearchLookup, { filterLookupOptions } from '../../components/SearchLookup';
import { searchHocVien } from '../hoc-vien/api';
import type { HocVienListItem } from '../hoc-vien/types';
import { getCourseAssignmentDetail } from './api';
import AssignmentCommandDialog from './dialogs/AssignmentCommandDialog';
import StudentHistoryDialog from './dialogs/StudentHistoryDialog';
import AssignmentExcelPanel from './panels/AssignmentExcelPanel';
import CourseGroupsPanel from './panels/CourseGroupsPanel';
import CourseHistoryPanel from './panels/CourseHistoryPanel';
import type {
  AssignmentOperation,
  AssignmentSelection,
  CourseAssignmentDetail,
  StudentAssignmentItem,
} from './types';
import {
  EmptyState,
  formatDate,
  PageMessage,
  Pager,
  ReferenceLabel,
  StatusBadge,
} from './ui';

const PAGE_SIZE = 25;

type CourseSection =
  | 'information'
  | 'students'
  | 'groups'
  | 'resources'
  | 'excel'
  | 'history'
  | 'completion';

interface AssignmentDialogState {
  operation: AssignmentOperation;
  student: StudentAssignmentItem | null;
}

const COURSE_SECTIONS: { key: CourseSection; number: number; label: string }[] = [
  { key: 'information', number: 1, label: 'Thông tin khóa' },
  { key: 'students', number: 2, label: 'Danh sách học viên' },
  { key: 'groups', number: 3, label: 'Nhóm đào tạo' },
  { key: 'resources', number: 4, label: 'Giáo viên và xe' },
  { key: 'excel', number: 5, label: 'Nhập/Xuất Excel' },
  { key: 'history', number: 6, label: 'Lịch sử' },
  { key: 'completion', number: 7, label: 'Hoàn thành khóa học' },
];

export default function CourseDetailPage() {
  const { khoaHocId } = useParams();
  const courseId = Number(khoaHocId);
  const { user } = useAuth();
  const canAssign = !!user && hasPermission(user.role, 'CanAssignStudents');
  const canBulkAssign = !!user && hasPermission(user.role, 'CanBulkAssignStudents');
  const canManageGroups = !!user && hasPermission(user.role, 'CanManageCourseGroups');
  const canViewHistory = !!user && hasPermission(user.role, 'CanViewAssignmentHistory');
  const canViewCompletion = !!user && hasPermission(user.role, 'CanViewCourseCompletionStatus');
  const canPreviewCompletion = !!user && hasPermission(user.role, 'CanPreviewCourseCompletion');
  const canCompleteCourse = !!user && hasPermission(user.role, 'CanCompleteCourse');
  const [activeSection, setActiveSection] = useState<CourseSection>('information');
  const [keywordDraft, setKeywordDraft] = useState('');
  const [selectedStudentLookup, setSelectedStudentLookup] = useState<HocVienListItem | null>(null);
  const [keyword, setKeyword] = useState('');
  const [groupId, setGroupId] = useState<number | null>(null);
  const [unassignedOnly, setUnassignedOnly] = useState(false);
  const [page, setPage] = useState(1);
  const [detail, setDetail] = useState<CourseAssignmentDetail | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [selectedIds, setSelectedIds] = useState<Set<number>>(new Set());
  const [allFiltered, setAllFiltered] = useState(false);
  const [assignmentDialog, setAssignmentDialog] = useState<AssignmentDialogState | null>(null);
  const [historyStudent, setHistoryStudent] = useState<StudentAssignmentItem | null>(null);
  const [studentLookupValidation, setStudentLookupValidation] = useState<string | null>(null);

  const load = useCallback(async (signal?: AbortSignal) => {
    if (!Number.isSafeInteger(courseId) || courseId <= 0) return;
    setLoading(true);
    setError(null);
    try {
      setDetail(await getCourseAssignmentDetail(courseId, {
        keyword,
        groupId,
        unassignedOnly,
        page,
        pageSize: PAGE_SIZE,
      }, signal));
    } catch (loadError) {
      if (!(loadError instanceof DOMException && loadError.name === 'AbortError')) {
        setError(loadError instanceof Error ? loadError.message : 'Không thể tải chi tiết khóa học.');
      }
    } finally {
      if (!signal?.aborted) setLoading(false);
    }
  }, [courseId, groupId, keyword, page, unassignedOnly]);

  useEffect(() => {
    const controller = new AbortController();
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const versionRefresh = useDataVersionRefresh({
    resources: ['hocVienVersion', 'khoaHocVersion', 'giaoVienVersion'],
    enabled: Number.isSafeInteger(courseId) && courseId > 0,
    onVersionChanged: async (_next, previous) => {
      if (previous) {
        setNotice(
          'Dữ liệu nguồn vừa thay đổi và danh sách đã được tải lại. '
          + 'Học viên mới không bị coi là lỗi; preview/confirm vẫn revalidate từng bản ghi và RowVersion.',
        );
      }
      await load();
    },
  });

  useEffect(() => {
    setSelectedIds(new Set());
    setAllFiltered(false);
  }, [groupId, keyword, page, unassignedOnly]);

  const visibleSelectedCount = useMemo(
    () => detail?.students.items.filter((row) => selectedIds.has(row.hocVienId)).length ?? 0,
    [detail?.students.items, selectedIds],
  );
  const selectionCount = allFiltered
    ? detail?.students.totalItems ?? 0
    : selectedIds.size;

  const selection: AssignmentSelection = allFiltered
    ? {
        mode: 'FILTER',
        hocVienIds: [],
        filter: {
          keyword: keyword || undefined,
          groupId,
          unassignedOnly,
        },
      }
    : {
        mode: 'IDS',
        hocVienIds: [...selectedIds],
      };

  const expectedRowVersions = useMemo(() => {
    if (allFiltered || !detail) return {};
    return Object.fromEntries(
      detail.students.items
        .filter((row) => selectedIds.has(row.hocVienId))
        .map((row) => [String(row.hocVienId), row.assignmentRowVersion]),
    );
  }, [allFiltered, detail, selectedIds]);

  if (!Number.isSafeInteger(courseId) || courseId <= 0) {
    return <PageMessage kind="error">Định danh khóa học không hợp lệ.</PageMessage>;
  }

  function submitStudentSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (keywordDraft.trim() && !selectedStudentLookup) {
      setStudentLookupValidation('Vui lòng chọn Học viên trong danh sách kết quả.');
      return;
    }
    setStudentLookupValidation(null);
    setPage(1);
    setKeyword(selectedStudentLookup?.maDangKy ?? '');
  }

  function toggleStudent(hocVienId: number) {
    setAllFiltered(false);
    setSelectedIds((current) => {
      const next = new Set(current);
      if (next.has(hocVienId)) next.delete(hocVienId);
      else next.add(hocVienId);
      return next;
    });
  }

  function toggleCurrentPage() {
    if (!detail) return;
    setAllFiltered(false);
    const pageIds = detail.students.items.map((row) => row.hocVienId);
    const allSelected = pageIds.every((id) => selectedIds.has(id));
    setSelectedIds((current) => {
      const next = new Set(current);
      pageIds.forEach((id) => allSelected ? next.delete(id) : next.add(id));
      return next;
    });
  }

  function clearSelection() {
    setSelectedIds(new Set());
    setAllFiltered(false);
  }

  function openBulk(operation: AssignmentOperation) {
    if (selectionCount === 0) return;
    setAssignmentDialog({ operation, student: null });
  }

  return (
    <div className="assignment-page">
      <div className="assignment-breadcrumb">
        <Link to="/khoa-hoc">Khóa học</Link>
        <span>/</span>
        <strong>{detail?.course.maKhoa || `#${courseId}`}</strong>
      </div>

      <section className="panel assignment-hero">
        <div>
          <span className="assignment-eyebrow">
            {detail?.course.sourceProfileCode || 'Đang tải'} · Chi tiết phân công
          </span>
          <h2>{detail?.course.tenKhoa || detail?.course.maKhoa || 'Đang tải khóa học...'}</h2>
          <p>
            Mọi thao tác dùng đúng KhoaHocId, SourceProfileCode và RowVersion;
            số lượng có thể thay đổi khi cơ sở tiếp tục nhập học viên.
          </p>
        </div>
        <div className="assignment-hero__actions">
          <span className="assignment-readonly-chip">Thông tin khóa chỉ đọc nguồn</span>
          <button
            type="button"
            className="btn btn--ghost"
            onClick={() => void versionRefresh.reload()}
            disabled={loading || versionRefresh.checking}
          >
            {loading || versionRefresh.checking ? 'Đang tải...' : 'Tải dữ liệu mới nhất'}
          </button>
        </div>
      </section>

      {error && <PageMessage kind="error">{error}</PageMessage>}
      {notice && <PageMessage kind="success">{notice}</PageMessage>}
      {versionRefresh.error && <PageMessage kind="warning">{versionRefresh.error}</PageMessage>}

      {detail && (
        <section className="assignment-summary-grid" aria-label="Tổng hợp phân công">
          <SummaryCard label="Học viên trong khóa" value={detail.summary.learnerCount} />
          <SummaryCard label="Đã có phân công" value={detail.summary.assignedCount} />
          <SummaryCard label="Chưa phân công" value={detail.summary.unassignedCount} warning={detail.summary.unassignedCount > 0} />
          <SummaryCard label="Cần kiểm tra" value={detail.summary.manualReviewCount} warning={detail.summary.manualReviewCount > 0} />
        </section>
      )}

      <nav className="assignment-section-nav" aria-label="Bảy phần chi tiết khóa">
        {COURSE_SECTIONS.map((section) => (
          <button
            key={section.key}
            type="button"
            className={activeSection === section.key ? 'is-active' : ''}
            onClick={() => setActiveSection(section.key)}
          >
            <span>{section.number}</span>
            {section.label}
          </button>
        ))}
      </nav>

      {!detail && loading && <div className="panel"><EmptyState>Đang tải dữ liệu khóa học...</EmptyState></div>}

      {detail && activeSection === 'information' && (
        <CourseInformation detail={detail} />
      )}

      {detail && activeSection === 'students' && (
        <section className="assignment-stack">
          <form className="toolbar" onSubmit={submitStudentSearch}>
            <div className="toolbar__row">
              <SearchLookup
                id="assignment-student-lookup"
                label="Học viên"
                value={selectedStudentLookup}
                inputValue={keywordDraft}
                onInputValueChange={(value) => {
                  setKeywordDraft(value);
                  setStudentLookupValidation(null);
                }}
                onChange={(option) => {
                  setSelectedStudentLookup(option);
                  setStudentLookupValidation(null);
                }}
                loadOptions={async (lookupKeyword, signal) => {
                  const result = await searchHocVien({
                    keyword: lookupKeyword,
                    maKhoa: detail.course.maKhoa,
                    page: 1,
                    pageSize: 20,
                  }, signal);
                  return result.items;
                }}
                getKey={(option) => option.hocVienId}
                getLabel={(option) => `${option.maDangKy} · ${option.hoVaTen}`}
                getDescription={(option) => [option.soCccd, option.ngaySinh ? formatDate(option.ngaySinh) : null].filter(Boolean).join(' · ')}
                placeholder="Mã đăng ký, họ tên hoặc CCCD"
                emptyText="Không có học viên phù hợp trong khóa"
                errorText="Không tải được danh sách Học viên."
              />
              <SearchLookup
                id="assignment-group-filter-lookup"
                label="Nhóm đào tạo"
                value={detail.groups.find((group) => group.groupId === groupId) ?? null}
                onChange={(option) => {
                  setGroupId(option?.groupId ?? null);
                  setPage(1);
                }}
                loadOptions={async (lookupKeyword) => filterLookupOptions(
                  detail.groups,
                  lookupKeyword,
                  (group) => `${group.maNhom} ${group.tenNhom}`,
                  20,
                )}
                getKey={(option) => option.groupId}
                getLabel={(option) => `${option.maNhom} · ${option.tenNhom}`}
                getDescription={(option) => `${option.studentCount} học viên${option.isActive ? '' : ' · ngừng dùng'}`}
                placeholder="Mã nhóm hoặc tên nhóm"
                emptyText="Không có nhóm phù hợp"
                errorText="Không tải được danh sách Nhóm."
              />
              <label className="assignment-check">
                <input
                  type="checkbox"
                  checked={unassignedOnly}
                  onChange={(event) => {
                    setUnassignedOnly(event.target.checked);
                    setPage(1);
                  }}
                />
                Chỉ học viên chưa phân công
              </label>
              <div className="toolbar__actions">
                <button type="submit" className="btn btn--primary" disabled={loading}>Lọc</button>
                <button type="button" className="btn btn--ghost" onClick={() => void load()} disabled={loading}>Tải lại</button>
              </div>
            </div>
          </form>

          {studentLookupValidation && <PageMessage kind="warning">{studentLookupValidation}</PageMessage>}

          <div className="panel assignment-selection-bar">
            <div>
              <strong>{selectionCount.toLocaleString('vi-VN')} học viên được chọn</strong>
              <p>
                {allFiltered
                  ? 'Server sẽ khóa và materialize toàn bộ kết quả lọc hiện tại khi preview.'
                  : 'Vùng chọn theo HocVienId; server revalidate từng bản ghi khi preview và confirm.'}
              </p>
            </div>
            <div className="assignment-row-actions">
              {visibleSelectedCount === detail.students.items.length && detail.students.totalItems > detail.students.items.length && !allFiltered && (
                <button type="button" className="btn btn--secondary" onClick={() => setAllFiltered(true)}>
                  Chọn toàn bộ {detail.students.totalItems.toLocaleString('vi-VN')} kết quả lọc
                </button>
              )}
              {selectionCount > 0 && (
                <button type="button" className="btn btn--ghost" onClick={clearSelection}>Bỏ chọn</button>
              )}
              {canBulkAssign && (
                <>
                  <button type="button" className="btn btn--secondary" disabled={selectionCount === 0} onClick={() => openBulk('PUT_IN_GROUP')}>
                    Đưa vào nhóm
                  </button>
                  <button type="button" className="btn btn--primary" disabled={selectionCount === 0} onClick={() => openBulk('BULK_ASSIGN')}>
                    Phân công hàng loạt
                  </button>
                </>
              )}
            </div>
          </div>

          <StudentTable
            detail={detail}
            selectedIds={selectedIds}
            allFiltered={allFiltered}
            loading={loading}
            canAssign={canAssign}
            canViewHistory={canViewHistory}
            onTogglePage={toggleCurrentPage}
            onToggleStudent={toggleStudent}
            onOverride={(student) => setAssignmentDialog({ operation: 'STUDENT_OVERRIDE', student })}
            onHistory={setHistoryStudent}
            onPage={setPage}
          />
        </section>
      )}

      {detail && activeSection === 'groups' && (
        <CourseGroupsPanel
          mode="groups"
          course={detail.course}
          groups={detail.groups}
          lookups={detail.lookups}
          canManage={canManageGroups}
          onChanged={(message) => {
            setNotice(message);
            clearSelection();
            void load();
          }}
          onError={(message) => {
            setError(message);
            void load();
          }}
        />
      )}

      {detail && activeSection === 'resources' && (
        <CourseGroupsPanel
          mode="resources"
          course={detail.course}
          groups={detail.groups}
          lookups={detail.lookups}
          canManage={canManageGroups}
          onChanged={(message) => {
            setNotice(message);
            clearSelection();
            void load();
          }}
          onError={(message) => {
            setError(message);
            void load();
          }}
        />
      )}

      {detail && activeSection === 'excel' && (
        <AssignmentExcelPanel
          course={detail.course}
          onChanged={(message) => {
            setNotice(message);
            clearSelection();
            void load();
          }}
          onError={setError}
        />
      )}

      {detail && activeSection === 'history' && (
        canViewHistory
          ? <CourseHistoryPanel courseId={courseId} />
          : <PageMessage kind="warning">Bạn không có quyền xem lịch sử phân công.</PageMessage>
      )}

      {activeSection === 'completion' && (
        canViewCompletion
          ? <CourseCompletionPanel
              courseId={courseId}
              sourceProfileCode={detail?.course.sourceProfileCode ?? ''}
              canPreview={canPreviewCompletion}
              canComplete={canCompleteCourse}
            />
          : <PageMessage kind="warning">Bạn không có quyền xem trạng thái hoàn thành khóa học.</PageMessage>
      )}

      {assignmentDialog && detail && (
        <AssignmentCommandDialog
          course={detail.course}
          groups={detail.groups}
          lookups={detail.lookups}
          operation={assignmentDialog.operation}
          student={assignmentDialog.student}
          selection={assignmentDialog.student
            ? { mode: 'IDS', hocVienIds: [assignmentDialog.student.hocVienId] }
            : selection}
          expectedRowVersions={assignmentDialog.student
            ? { [String(assignmentDialog.student.hocVienId)]: assignmentDialog.student.assignmentRowVersion }
            : expectedRowVersions}
          onClose={() => setAssignmentDialog(null)}
          onConfirmed={(message) => {
            setAssignmentDialog(null);
            clearSelection();
            setNotice(message);
            void load();
          }}
          onConflict={(message) => {
            setAssignmentDialog(null);
            clearSelection();
            setError(message);
            void load();
          }}
        />
      )}

      {historyStudent && (
        <StudentHistoryDialog
          student={historyStudent}
          onClose={() => setHistoryStudent(null)}
        />
      )}
    </div>
  );
}

function SummaryCard({
  label,
  value,
  warning = false,
}: {
  label: string;
  value: number;
  warning?: boolean;
}) {
  return (
    <article className={`panel assignment-summary-card${warning ? ' is-warning' : ''}`}>
      <span>{label}</span>
      <strong>{value.toLocaleString('vi-VN')}</strong>
    </article>
  );
}

function CourseInformation({ detail }: { detail: CourseAssignmentDetail }) {
  const course = detail.course;
  return (
    <section className="panel assignment-information">
      <header className="assignment-section-heading">
        <div>
          <span className="assignment-eyebrow">1 · Thông tin khóa</span>
          <h3>{course.maKhoa} · {course.tenKhoa || 'Chưa có tên khóa'}</h3>
        </div>
        <StatusBadge
          active={course.isActive}
          manualReview={course.manualReviewCount > 0}
          label={course.trangThai}
        />
      </header>
      <dl className="assignment-facts">
        <Fact label="KhoaHocId" value={String(course.khoaHocId)} />
        <Fact label="Nguồn chính xác" value={course.sourceProfileCode} />
        <Fact label="Mã khóa" value={course.maKhoa} />
        <Fact label="Tên khóa" value={course.tenKhoa} />
        <Fact label="Hạng đào tạo" value={course.hangDaoTao} />
        <Fact label="Loại / hình thức" value={course.loaiDaoTao} />
        <Fact label="Ngày khai giảng" value={formatDate(course.ngayKhaiGiang)} />
        <Fact label="Ngày bế giảng" value={formatDate(course.ngayBeGiang)} />
        <Fact label="Số quyết định" value={course.soQuyetDinh} />
        <Fact label="Quyền sở hữu" value="CSDL nguồn · chỉ đọc" />
      </dl>
    </section>
  );
}

function Fact({ label, value }: { label: string; value: string | null }) {
  return (
    <div>
      <dt>{label}</dt>
      <dd>{value || '—'}</dd>
    </div>
  );
}

function StudentTable({
  detail,
  selectedIds,
  allFiltered,
  loading,
  canAssign,
  canViewHistory,
  onTogglePage,
  onToggleStudent,
  onOverride,
  onHistory,
  onPage,
}: {
  detail: CourseAssignmentDetail;
  selectedIds: Set<number>;
  allFiltered: boolean;
  loading: boolean;
  canAssign: boolean;
  canViewHistory: boolean;
  onTogglePage: () => void;
  onToggleStudent: (id: number) => void;
  onOverride: (student: StudentAssignmentItem) => void;
  onHistory: (student: StudentAssignmentItem) => void;
  onPage: (page: number) => void;
}) {
  const pageSelected = detail.students.items.length > 0
    && detail.students.items.every((row) => selectedIds.has(row.hocVienId));

  if (detail.students.items.length === 0) {
    return <div className="panel"><EmptyState>Không có học viên phù hợp bộ lọc.</EmptyState></div>;
  }

  return (
    <>
      <div className="table-wrap">
        <table className="table assignment-table assignment-table--students">
          <thead>
            <tr>
              <th>
                <input
                  type="checkbox"
                  checked={allFiltered || pageSelected}
                  onChange={onTogglePage}
                  aria-label="Chọn toàn bộ học viên trên trang"
                />
              </th>
              <th>Học viên</th>
              <th>Nhóm</th>
              <th>Người nhận hồ sơ</th>
              <th>Giáo viên đứng lớp</th>
              <th>Xe tập lái</th>
              <th>Xe bài số 10</th>
              <th>Trạng thái</th>
              <th>Thao tác</th>
            </tr>
          </thead>
          <tbody>
            {detail.students.items.map((student) => (
              <tr
                key={student.hocVienId}
                className={student.assignmentStatus === 'MANUAL_REVIEW' ? 'is-manual-review' : ''}
              >
                <td>
                  <input
                    type="checkbox"
                    checked={allFiltered || selectedIds.has(student.hocVienId)}
                    onChange={() => onToggleStudent(student.hocVienId)}
                    aria-label={`Chọn ${student.maDangKy}`}
                  />
                </td>
                <td>
                  <strong>{student.maDangKy}</strong>
                  <span className="assignment-cell-note">{student.hoTen}</span>
                  <span className="assignment-cell-note">
                    {formatDate(student.ngaySinh)} · {student.hangHoc || 'Chưa có hạng'}
                  </span>
                </td>
                <td>{student.groupCode || <span className="assignment-muted">Chưa vào nhóm</span>}</td>
                <td><ReferenceLabel value={student.dossierReceiver} /></td>
                <td><ReferenceLabel value={student.classTeacher} overridden={student.overrideClassTeacher} /></td>
                <td><ReferenceLabel value={student.trainingVehicle} overridden={student.overrideTrainingVehicle} /></td>
                <td><ReferenceLabel value={student.figure10Vehicle} overridden={student.overrideFigure10Vehicle} /></td>
                <td>
                  <StatusBadge
                    active={student.assignmentStatus === 'ASSIGNED'}
                    manualReview={student.assignmentStatus === 'MANUAL_REVIEW'}
                    label={student.assignmentStatus === 'UNASSIGNED' ? 'Chưa phân công' : 'Đã phân công'}
                  />
                  {student.warnings.map((warning, index) => (
                    <small className="assignment-cell-warning" key={`${warning}:${index}`}>{warning}</small>
                  ))}
                </td>
                <td>
                  <div className="assignment-row-actions">
                    {canAssign && (
                      <button type="button" className="btn btn--ghost btn--sm" onClick={() => onOverride(student)}>
                        Gán / ghi đè
                      </button>
                    )}
                    {canViewHistory && (
                      <button type="button" className="btn btn--ghost btn--sm" onClick={() => onHistory(student)}>
                        Lịch sử
                      </button>
                    )}
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <Pager result={detail.students} onPage={onPage} disabled={loading} />
    </>
  );
}
