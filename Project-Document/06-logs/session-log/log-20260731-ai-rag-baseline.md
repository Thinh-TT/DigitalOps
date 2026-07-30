# Session Log - 2026-07-31 AI RAG Baseline

- **Ngày**: 2026-07-31
- **Người thực hiện**: Codex
- **Task liên quan**: AI RAG documentation baseline (chưa có ID trên Task Board)
- **Loại**: Decision
- **Trạng thái**: Hoàn thành

## Bối cảnh

DigitalOps sẽ dùng RAG và gọi LLM cho gợi ý điều phối, sinh nháp và hỗ trợ thẩm định. Các lựa chọn về provider, model, embedding, vector store, chunking và vận hành chưa được thành viên phụ trách AI của team quyết định.

## Quyết định

- Tạo 02-architecture/03-ai-rag-design.md ở trạng thái Draft làm baseline cho ranh giới hệ thống, lifecycle dữ liệu, guardrail, approval gate và acceptance criteria.
- Không chọn công nghệ RAG cụ thể, không thêm schema/index/vector migration, public endpoint hoặc UI mới trước khi AI team phê duyệt.
- Giữ PostgreSQL full-text search là contract FR-016; RAG không thay thế search hiện tại.
- Cập nhật mô tả AI trong Ideas and Scope, Functional Requirements, API Specification, AGENT.md và README để phản ánh RAG + LLM cùng trạng thái chờ duyệt.

## Tác động

AI team có một nơi tập trung để chốt các quyết định kỹ thuật và tiêu chí an toàn trước khi triển khai. Backend/frontend tiếp tục dùng contract API hiện có; lỗi AI vẫn trả 503 và không mutation.

## Theo dõi tiếp

- Thành viên phụ trách AI chọn và phê duyệt provider/model, embedding, vector store, nguồn tri thức, security/retention, SLO/cost và evaluation.
- Sau phê duyệt, cập nhật Database Designer, API Specification, task board và session log với các quyết định chính thức.
