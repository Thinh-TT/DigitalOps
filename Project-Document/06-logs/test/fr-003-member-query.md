# Mẫu thử FR-003 — Tra cứu hội viên

## Mục tiêu

Xác nhận Administrator/Clerk xem, tìm kiếm và phân trang hội viên đúng quyền; các
role khác chỉ dùng lookup theo phạm vi được phép và không làm thay đổi dữ liệu.

## Tiền điều kiện

- Có ba hội viên synthetic: hai Active, một Inactive; tên, phone và email khác nhau.
- Có ít nhất một văn bản đi liên kết với một hội viên synthetic.
- Role thử: Administrator, Clerk, Drafter và anonymous.

## Ca thử

| ID | Ca thử | Bước thực hiện | Kết quả mong đợi |
| --- | --- | --- | --- |
| FR003-01 | Danh sách mặc định | Mở `/members?page=1&pageSize=20` bằng Administrator/Clerk | `200`; cấu trúc paging đúng; dữ liệu sắp xếp ổn định |
| FR003-02 | Tìm theo tên/phone/email | Tìm lần lượt bằng một phần tên, phone và email synthetic | Chỉ trả các bản ghi khớp; tìm kiếm không phân biệt hoa/thường theo contract |
| FR003-03 | Lọc trạng thái | Chọn Active rồi Inactive | Mọi item đúng status đã chọn; tổng số cập nhật theo filter |
| FR003-04 | Phân trang biên | Dùng `page=1&pageSize=1`, rồi page vượt total | Metadata đúng; page vượt phạm vi trả items rỗng, không lỗi server |
| FR003-05 | Chi tiết và not found | Mở ID hợp lệ, sau đó GUID không tồn tại | ID hợp lệ trả đủ hồ sơ; GUID lạ trả `404 ProblemDetails` |
| FR003-06 | Văn bản liên quan | Từ chi tiết hội viên mở danh sách outgoing theo `relatedMemberId` | Chỉ văn bản liên kết hội viên đó được trả/mở đúng detail |
| FR003-07 | Lookup Active | Gọi `/members/lookup` | Chỉ hội viên Active; không có hội viên Inactive |
| FR003-08 | Phân quyền | Gọi danh sách/chi tiết bằng Drafter và anonymous; gọi lookup bằng Drafter | Danh sách/chi tiết trả `403`/`401`; Drafter được dùng lookup Active phục vụ tạo outgoing |

## Tiêu chí kết thúc

- Không test bằng dữ liệu cá nhân thật.
- Các thao tác chỉ đọc không thay `updatedAt` hoặc status.
