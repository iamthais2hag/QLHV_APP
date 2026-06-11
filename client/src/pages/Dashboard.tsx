const SUMMARY_CARDS = [
  { label: 'Học viên đang theo học' },
  { label: 'Khóa học đang mở' },
  { label: 'Giáo viên' },
  { label: 'Xe tập lái' },
  { label: 'Kỳ sát hạch sắp tới' },
  { label: 'Hồ sơ chờ xử lý' },
];

export default function Dashboard() {
  return (
    <div className="card-grid">
      {SUMMARY_CARDS.map((card) => (
        <div className="card" key={card.label}>
          <p className="card__label">{card.label}</p>
          <p className="card__value">—</p>
        </div>
      ))}
    </div>
  );
}
