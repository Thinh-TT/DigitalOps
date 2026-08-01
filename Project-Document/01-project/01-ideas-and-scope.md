## Tên Đề Tài

DigitalOps – Hệ thống điều hành số văn bản và hồ sơ hội viên tích hợp AI cho UBMTTQ cấp phường

## Ý Tưởng Sản Phẩm

DigitalOps là một hệ thống điều hành số dành cho tổ chức Mặt trận Tổ quốc cấp phường, tập trung vào hai việc cốt lõi trong công tác hành chính hằng ngày: quản lý hồ sơ hội viên và xử lý văn bản. Thay vì số hóa rời rạc từng việc, hệ thống nối liền thành một luồng khép kín: văn bản đến có thời hạn được tiếp nhận, điều phối và nhắc hạn tự động; khi cần phản hồi, cán bộ soạn thảo văn bản với sự hỗ trợ của AI, hệ thống tự kiểm tra thể thức trước khi trình phê duyệt, và văn bản sau khi lưu trữ được gắn liền với hồ sơ hội viên/vụ việc liên quan để tra cứu về sau. AI và tự động hóa được tích hợp ở đúng những điểm tạo giá trị thật — gợi ý điều phối, hỗ trợ soạn thảo, kiểm tra chuẩn hóa — thay vì phủ khắp mọi tính năng.

## Phạm Vi Tổng Thể

**Trong phạm vi (core, cam kết hoàn thành):**

- Số hóa hồ sơ hội viên (CRUD + tìm kiếm + import, làm nền dữ liệu)
- Tiếp nhận văn bản đến có thời hạn: số hóa/vào sổ, điều phối tự động (AI gợi ý người xử lý, người dùng xác nhận), nhắc hạn tự động
- Soạn thảo văn bản: khởi tạo theo mẫu, tự động kéo dữ liệu hội viên liên quan
- AI hỗ trợ soạn thảo văn bản
- Thẩm định & chuẩn hóa văn bản: kiểm tra thể thức/hình thức (không kiểm tra đúng-sai nội dung/pháp lý)
- Phê duyệt & lưu trữ: gắn văn bản trở lại hồ sơ hội viên/vụ việc liên quan

**Mở rộng (chỉ làm nếu còn thời gian, không cam kết):**

- AI tự trích xuất hạn xử lý từ nội dung văn bản đến
- AI hỗ trợ viết bài tin hoạt động từ hình ảnh

**Ngoài phạm vi (đã cắt):**

- Membership nâng cao, Welfare/quỹ, CitizenPortal, Training
- Các việc văn phòng thuần CRUD (con dấu, tài sản, lịch họp thường) trừ khi gắn trực tiếp với văn bản có hạn
- Điều phối tự động "cứng" không qua xác nhận người dùng

## Công Nghệ Sử Dụng

**Backend:** ASP.NET Core Web API (.NET), Entity Framework Core, PostgreSQL (một database, một schema) — một project API duy nhất, tổ chức theo feature (Members, IncomingDocuments, Drafting, Review, Approval), không dùng module system hay architecture test enforce ranh giới.

**Frontend:** React + Vite + TypeScript + Ant Design.

**Xác thực:** ASP.NET Core Identity + JWT (không dùng Keycloak riêng).

**AI:** kiến trúc RAG local-first do DigitalOps điều phối, gọi Ollama qua HTTP với Qwen3 và dùng Qdrant làm derived vector index cho gợi ý điều phối, sinh nháp và hỗ trợ thẩm định. Quyết định đã khóa nhưng còn chờ evaluation gate và Project Owner phê duyệt cho MVP/demo theo 02-architecture/03-ai-rag-design.md. PostgreSQL full-text search vẫn là chức năng tìm kiếm chính thức của MVP.

**Tự động hóa nhắc hạn:** `IHostedService` chạy định kỳ trong chính API.

**Lưu trữ file:** local disk có tổ chức thư mục, hoặc bucket S3-compatible nếu cần.

**Môi trường:** Development và Production — không dùng pipeline release với immutable manifest.

## Tính Năng MVP

### 1. Hồ sơ hội viên

- Số hóa, lưu trữ và tìm kiếm hồ sơ hội viên
- Import dữ liệu hội viên có sẵn
- Là nguồn dữ liệu được kéo tự động vào văn bản khi khởi tạo

### 2. Văn bản đến & điều phối tự động

- Nguồn văn bản đến: từ các đơn vị/cơ quan (UBMTTQ cấp trên, đoàn thể phối hợp, cơ quan nhà nước liên quan) qua kênh sẵn có (công văn giấy, email công vụ, hệ thống liên thông nếu có) — **không phải kênh để công dân/hội viên tự nộp trực tiếp**
- Người thao tác là cán bộ văn thư (nội bộ): nhận văn bản, nhập số hiệu/nơi gửi/trích yếu/ngày nhận/thời hạn, scan/upload nếu là bản giấy
- Điều phối tự động: AI gợi ý người xử lý phù hợp dựa trên trích yếu/loại văn bản, cán bộ văn thư xác nhận
- Nhắc hạn tự động trước và khi quá hạn xử lý cho người được giao
- Nếu cần trả lời bằng văn bản, nối sang luồng soạn thảo (mục 3), gắn ngược lại văn bản đến gốc

### 3. Soạn thảo, AI hỗ trợ, thẩm định & lưu trữ

- Khởi tạo văn bản theo mẫu, tự động kéo dữ liệu hội viên liên quan
- AI hỗ trợ sinh bản nháp văn bản
- Thẩm định & chuẩn hóa thể thức (rule + AI hỗ trợ phát hiện lỗi), trả lại chỉnh sửa nếu chưa đạt
- Phê duyệt và lưu trữ, gắn văn bản với hồ sơ hội viên/vụ việc liên quan
