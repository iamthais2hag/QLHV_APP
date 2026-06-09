interface ModulePageProps {
  title: string;
  description: string;
}

/**
 * Khung trang dùng chung cho các phân hệ. Nội dung nghiệp vụ sẽ được bổ sung ở các bước sau.
 */
export default function ModulePage({ title, description }: ModulePageProps) {
  return (
    <div>
      <div className="page__head">
        <h2 className="page__title">{title}</h2>
        <p className="page__subtitle">{description}</p>
      </div>

      <div className="panel">
        <p className="card__label" style={{ margin: 0 }}>
          Chưa có dữ liệu để hiển thị.
        </p>
      </div>
    </div>
  );
}
