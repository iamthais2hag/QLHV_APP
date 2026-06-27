export interface MenuItem {
  /** Đường dẫn route */
  path: string;
  /** Nhãn tiếng Việt hiển thị trên sidebar và top bar */
  label: string;
  /** Mô tả ngắn hiển thị dưới tiêu đề ở top bar */
  description: string;
  /** Biểu tượng hiển thị cạnh nhãn */
  icon: string;
}

export const MENU_ITEMS: MenuItem[] = [
  {
    path: '/',
    label: 'Tổng quan',
    description: 'Bảng tổng hợp tình hình đào tạo và quản lý.',
    icon: '▦',
  },
  {
    path: '/hoc-vien',
    label: 'Học viên',
    description: 'Tra cứu danh sách và hồ sơ học viên.',
    icon: '🎓',
  },
  {
    path: '/in-the-hoc-vien',
    label: 'In thẻ học viên',
    description: 'Chọn học viên, thiết lập tiêu đề và in thẻ theo mẫu chính thức.',
    icon: '▤',
  },
  {
    path: '/khoa-hoc',
    label: 'Khóa học',
    description: 'Quản lý các khóa học và lịch đào tạo.',
    icon: '📚',
  },
  {
    path: '/giao-vien',
    label: 'Giáo viên',
    description: 'Quản lý giáo viên và phân công giảng dạy.',
    icon: '👩‍🏫',
  },
  {
    path: '/xe-tap-lai',
    label: 'Xe tập lái',
    description: 'Quản lý phương tiện và phân bổ xe tập lái.',
    icon: '🚗',
  },
  {
    path: '/kiem-tra-du-lieu',
    label: 'Kiểm tra dữ liệu',
    description: 'Đối chiếu và kiểm tra tính hợp lệ của dữ liệu.',
    icon: '✔',
  },
  {
    path: '/ket-qua-tot-nghiep',
    label: 'Kết quả tốt nghiệp',
    description: 'Quản lý kết quả tốt nghiệp của học viên.',
    icon: '🏆',
  },
  {
    path: '/ky-sat-hach',
    label: 'Kỳ sát hạch',
    description: 'Quản lý các kỳ sát hạch và kết quả.',
    icon: '📝',
  },
  {
    path: '/dang-ky-thi-lai',
    label: 'Đăng ký thi lại',
    description: 'Quản lý đăng ký và lịch thi lại.',
    icon: '↻',
  },
  {
    path: '/chuyen-khoa',
    label: 'Chuyển khóa',
    description: 'Quản lý chuyển dữ liệu học viên giữa các khóa.',
    icon: '↪',
  },
  {
    path: '/xuat-word-excel',
    label: 'Xuất Word/Excel',
    description: 'Kết xuất biểu mẫu và báo cáo ra Word/Excel.',
    icon: '📄',
  },
  {
    path: '/the-phu-hieu',
    label: 'JP2/XML/Thẻ/Phù hiệu',
    description: 'Quản lý JP2/XML, thẻ học viên và phù hiệu giáo viên.',
    icon: '▣',
  },
  {
    path: '/dong-bo-v2',
    label: 'Đồng bộ dữ liệu',
    description: 'Đồng bộ dữ liệu một chiều từ nguồn V2.',
    icon: '🔄',
  },
  {
    path: '/cau-hinh-ket-noi-csdt',
    label: 'Cấu hình kết nối CSDT',
    description: 'Quản lý 7 profile kết nối CSDT/DATA/QLHV_APP an toàn.',
    icon: '🔌',
  },
  {
    path: '/tai-lieu-scan',
    label: 'Tài liệu scan/PDF',
    description: 'Quản lý tài liệu scan và tệp PDF.',
    icon: '🗂',
  },
  {
    path: '/nhat-ky-he-thong',
    label: 'Nhật ký hệ thống',
    description: 'Theo dõi nhật ký thao tác và sự kiện hệ thống.',
    icon: '📋',
  },
  {
    path: '/cau-hinh',
    label: 'Cấu hình',
    description: 'Thiết lập tham số và cấu hình hệ thống.',
    icon: '⚙',
  },
];
