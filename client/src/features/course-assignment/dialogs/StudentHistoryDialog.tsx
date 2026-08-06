import { useEffect, useState } from 'react';
import { getStudentAssignmentHistory } from '../api';
import type {
  AssignmentHistoryItem,
  StudentAssignmentItem,
} from '../types';
import {
  EmptyState,
  formatDateTime,
  Modal,
  PageMessage,
  ReferenceLabel,
} from '../ui';

export default function StudentHistoryDialog({
  student,
  onClose,
}: {
  student: StudentAssignmentItem;
  onClose: () => void;
}) {
  const [history, setHistory] = useState<AssignmentHistoryItem[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    getStudentAssignmentHistory(student.hocVienId, controller.signal)
      .then(setHistory)
      .catch((historyError) => {
        if (!(historyError instanceof DOMException && historyError.name === 'AbortError')) {
          setError(historyError instanceof Error ? historyError.message : 'Không thể tải lịch sử phân công.');
        }
      });
    return () => controller.abort();
  }, [student.hocVienId]);

  return (
    <Modal title={`Lịch sử phân công · ${student.maDangKy}`} onClose={onClose} wide>
      <PageMessage kind="info">
        {student.hoTen} · {student.sourceProfileCode}/{student.maKhoa} · HocVienId {student.hocVienId}
      </PageMessage>
      {error && <PageMessage kind="error">{error}</PageMessage>}
      {!history && !error && <EmptyState>Đang tải lịch sử...</EmptyState>}
      {history?.length === 0 && <EmptyState>Học viên chưa có snapshot phân công.</EmptyState>}
      {!!history?.length && (
        <div className="assignment-history-snapshots">
          {history.map((snapshot) => (
            <article key={snapshot.assignmentId} className={snapshot.isCurrent ? 'is-current' : ''}>
              <header>
                <div>
                  <strong>{snapshot.isCurrent ? 'Snapshot hiện hành' : 'Snapshot lịch sử'}</strong>
                  <span>
                    {formatDateTime(snapshot.effectiveFromUtc)}
                    {' → '}
                    {snapshot.effectiveToUtc ? formatDateTime(snapshot.effectiveToUtc) : 'hiện tại'}
                  </span>
                </div>
                <span className="assignment-source">{snapshot.source}</span>
              </header>
              <div className="assignment-history-snapshot__grid">
                <span>Nhóm <ReferenceLabel value={snapshot.group} /></span>
                <span>Người nhận HS <ReferenceLabel value={snapshot.dossierReceiver} /></span>
                <span>
                  Giáo viên
                  <ReferenceLabel value={snapshot.classTeacher} overridden={snapshot.overrideClassTeacher} />
                </span>
                <span>
                  Xe tập lái
                  <ReferenceLabel value={snapshot.trainingVehicle} overridden={snapshot.overrideTrainingVehicle} />
                </span>
                <span>
                  Xe bài 10
                  <ReferenceLabel value={snapshot.figure10Vehicle} overridden={snapshot.overrideFigure10Vehicle} />
                </span>
              </div>
              <footer>
                {snapshot.actor || 'Hệ thống'} · {snapshot.reason || 'Không có lý do'}
              </footer>
            </article>
          ))}
        </div>
      )}
    </Modal>
  );
}
