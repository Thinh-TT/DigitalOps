# Mẫu thử FR-009 — AI gợi ý và xác nhận điều phối

## Mục tiêu

Xác nhận AI chỉ gợi ý Staff từ nguồn được phép; Clerk luôn là người xác nhận cuối,
và lỗi AI/Qdrant không tự đổi assignment hoặc status.

## Tiền điều kiện

- Provider Development, Ollama embedding và Qdrant hoạt động.
- Có incoming synthetic status `New`, nội dung không chứa PII thật.
- Có `STAFF-A` active với position/department phù hợp và một Staff inactive.
- Ghi snapshot trước test: status, assignedToStaff, suggestedStaff và updatedAt.

## Ca thử

| ID | Ca thử | Bước thực hiện | Kết quả mong đợi |
| --- | --- | --- | --- |
| FR009-01 | Sinh gợi ý thành công | Clerk chọn Sinh gợi ý AI | `200`; suggested Staff active, reason/confidence hợp lệ; UI chỉ hiển thị gợi ý |
| FR009-02 | Gợi ý không tự giao việc | Đọc lại incoming ngay sau FR009-01 | assignedToStaff/confirmedBy/confirmedAt chưa đổi; status vẫn `New` |
| FR009-03 | Xác nhận gợi ý | Clerk xác nhận Staff AI đề xuất | `200`; assignedToStaff đúng; confirmedBy là Clerk; status `New → InProgress` |
| FR009-04 | Chọn thủ công | Với document mới, bỏ qua AI và chọn `STAFF-A` rồi xác nhận | Assignment thành công; không cần suggestion; audit confirmation đầy đủ |
| FR009-05 | Giao lại | Clerk chọn Staff active khác cho document InProgress/Overdue | assignee đổi; status giữ InProgress/Overdue; không mất metadata cần giữ theo contract |
| FR009-06 | Candidate inactive | Làm Staff target inactive trước lúc xác nhận | `400`/`409`; assignment cũ và status không đổi |
| FR009-07 | AI lỗi/timeout | Trong test environment, dùng failure stub hoặc tạm ngắt provider/Qdrant rồi gọi suggestion | `503 ProblemDetails`; toàn bộ snapshot nghiệp vụ không đổi; UI vẫn cho chọn Staff thủ công |
| FR009-08 | Không đủ evidence | Dùng incoming synthetic rất mơ hồ/không có candidate đạt threshold | Response theo contract `InsufficientEvidence` hoặc không có suggestedStaff; không assignment |
| FR009-09 | Document locked | Gọi suggestion/confirm trên Completed | `409`; không mutation |
| FR009-10 | Sai quyền/request trùng | Drafter/anonymous gọi endpoint; double-click khi request đang chạy | `403`/`401`; UI disable nút; không tạo hai mutation |
| FR009-11 | Data minimization | Kiểm tra structured logs/evidence của lượt AI | Không có password/token/raw prompt/PII ngoài allow-list; không index incoming vào Qdrant |

## Evidence AI

Ghi provider/model, elapsed time, số candidate và kết quả; không chép raw prompt,
API key hoặc dữ liệu nhận dạng thật vào evidence.

## Dọn dữ liệu

- Khôi phục Staff synthetic về trạng thái dự kiến.
- Xóa point Staff synthetic đúng ID nếu test đã tạo, không xóa các source type khác.
