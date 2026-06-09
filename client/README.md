# QLHV_APP - Client (Frontend)

Giao diện web nội bộ cho QLHV_APP. **Vite + React + TypeScript**.

## Công nghệ
- Vite + React 18 + TypeScript
- React Router (điều hướng phân hệ)
- Font: Be Vietnam Pro (tải qua Google Fonts trong `index.html`)
- Theme: xanh dương / trắng (Thành Công)
- Bố cục: sidebar trái + header trên + vùng nội dung chính, responsive cho desktop và tablet

## Cấu trúc
```
client/
  index.html
  vite.config.ts
  src/
    main.tsx              # điểm khởi động + Router
    App.tsx               # khai báo route theo menu
    navigation/menu.ts    # danh mục menu tiếng Việt (nguồn chung)
    layout/
      AppLayout.tsx       # khung sidebar + header + content
      Sidebar.tsx
      Header.tsx
    pages/
      Dashboard.tsx       # Tổng quan (khung thẻ thống kê)
      ModulePage.tsx      # khung trang dùng chung cho các phân hệ
    styles/
      global.css          # biến màu, font
      layout.css          # bố cục sidebar/header/content
```

## Lệnh chạy
```powershell
cd client
npm install      # cài dependencies (lần đầu)
npm run dev      # chạy môi trường phát triển (http://localhost:5173)
npm run build    # build production vào dist/
npm run preview  # xem thử bản build
```

> Phần khung giao diện đã sẵn sàng. Nội dung nghiệp vụ từng phân hệ
> (Học viên, Khóa học, ...) sẽ được bổ sung ở các bước tiếp theo.
> Frontend không chứa connection string; mọi dữ liệu lấy qua API backend an toàn.
