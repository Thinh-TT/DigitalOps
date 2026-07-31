# Session Log - 2026-07-31 AI RAG Provisional Decision

- **Ngày**: 2026-07-31
- **Task liên quan**: T0-00 — Chốt quyết định AI RAG
- **Loại**: Decision proposal
- **Trạng thái**: Đề xuất để AI review; chưa Approved, chưa triển khai.

## Bối cảnh

Team cần một phương án AI/RAG ban đầu chạy được trên laptop cấu hình thấp-trung và không gọi API AI từ nguồn bên thứ ba. Phương án cần giữ quyền thay đổi sau benchmark và review kiến trúc.

## Đề xuất tạm thời

- Chạy AI hoàn toàn cục bộ qua Ollama.
- Dùng `qwen3:4b-instruct` (quantized phù hợp) cho tác vụ gợi ý/sinh nháp và `qwen3-embedding:0.6b` cho embedding.
- Dùng Qdrant self-hosted làm vector database.
- Chỉ index Staff tối thiểu phục vụ điều phối, `DocumentTemplates` Active và `FormatRules`; chỉ dùng dữ liệu do team tạo hoặc đã được phê duyệt.
- AI chỉ gợi ý từ nguồn đã index/còn hiệu lực. Nếu không đủ bằng chứng, AI phải trả “không đủ dữ liệu để gợi ý”; người dùng luôn xác nhận kết quả.

## Tác động và điều kiện thay đổi

- Các lựa chọn trên là baseline tạm thời, không phải quyết định cuối cùng và không được dùng để tạo hạ tầng production, migration hay thay đổi API/UI khi tài liệu còn Draft.
- Phải benchmark tiếng Việt, retrieval, độ trễ và mức dùng RAM/VRAM trên laptop mục tiêu trước khi phê duyệt.
- Có thể thay đổi sau review hạ tầng hoặc đánh giá yêu cầu mở rộng. Nếu đổi embedding model hoặc dimension, phải re-embed toàn bộ index.

## Kiểm tra đã chạy

- Cập nhật `02-architecture/03-ai-rag-design.md` để phân biệt rõ đề xuất tạm thời với quyết định Approved.
- Kiểm tra `git diff --check` không báo lỗi whitespace.

## Theo dõi tiếp

- AI team review, ghi người duyệt/lý do/ngày hiệu lực và chỉ chuyển tài liệu sang Approved khi hoàn tất acceptance criteria của T0-00.
