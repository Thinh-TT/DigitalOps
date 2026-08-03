# Session log - RAG scraper adaptive chunking

- Ngày: 2026-08-03
- Phạm vi: `tools/rag-data-scraper`
- Trạng thái: Closed
- Liên quan: `log-20260803-rag-scraper-pagination-and-redirect-fixes.md`
- Task board: Không có task ID riêng; triển khai theo yêu cầu trực tiếp của Project Owner.

## Mục tiêu

Giảm cảnh báo chunk 512 token mà không ép cắt mọi cấu trúc tại target 448,
đồng thời giữ hard limit rõ ràng cho pipeline embedding.

## Quyết định và thay đổi

- Chuẩn hóa ba ngưỡng `target=448`, `soft_max=480`, `hard_max=512` và
  `overlap=64`; thứ tự được kiểm tra khi nạp cấu hình.
- Chunker v3 ưu tiên heading/đoạn rồi đến câu hoặc dòng danh sách. Một đơn vị
  ngữ nghĩa được giữ nguyên đến soft ceiling; nội dung dài hơn được tách theo từ,
  sau cùng theo ký tự nếu một token riêng lẻ không thể vừa ngân sách.
- Overlap chỉ dùng các đơn vị ngữ nghĩa hoàn chỉnh và bị bỏ khi làm chunk vượt
  soft ceiling.
- `chunk-sets.jsonl` bổ sung `soft_max_tokens` và `max_tokens` dưới dạng trường
  tương thích ngược. Worker hiện tại bỏ qua trường JSON bổ sung nên không cần
  migration database.
- RAG Inspector chỉ cảnh báo `TOKEN_BUDGET_EXCEEDED` khi package mới vượt soft
  ceiling và tạo lỗi `TOKEN_HARD_LIMIT_EXCEEDED` khi vượt hard limit. Package cũ
  tiếp tục dùng target làm ngưỡng cảnh báo.
- Web và CLI cùng khởi tạo chunker từ `config/settings.yaml`; trước đây web dùng
  default constructor thay vì truyền rõ cấu hình.

## Kiểm tra

- Regression test bao phủ câu nằm giữa target/soft, tách tại ranh giới câu,
  fallback theo từ, overlap không vượt soft, schema và Inspector soft/hard.
- Replay 23 HTML của job `ID2`: 60 chunk, trung bình 365.98 token, tối đa 447;
  không có chunk vượt target 448, soft 480 hoặc hard 512.
- Full test suite và compile check được ghi sau khi hoàn tất ở phần bàn giao.

## Phạm vi còn lại

- Token count vẫn dùng heuristic `vietnamese-word-1.3x`; chưa khóa theo tokenizer
  thật của embedding model.
- Job/package đã tạo trước chunker v3 không được tự ghi đè; cần job ID mới để
  nhận metadata soft/hard và chunk boundaries mới.
