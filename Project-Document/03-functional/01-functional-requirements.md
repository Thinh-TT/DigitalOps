# Functional Requirements

## 1. Mục tiêu tài liệu

### 1.1. Mục đích

- Mô tả yêu cầu chức năng đủ chi tiết để BA, Dev, Tester và UI triển khai thống nhất.
- Bám theo phạm vi trong [01-ideas-and-scope.md](../01-project/01-ideas-and-scope.md) và cấu trúc dữ liệu trong [01-database-designer.md](../02-architecture/01-database-designer.md).
- Bao phủ toàn bộ tính năng MVP: hồ sơ hội viên, văn bản đến, điều phối, nhắc hạn, soạn thảo, AI, thẩm định, phê duyệt, phát hành, lưu trữ và tra cứu.
- Dùng mã `FR-001` đến `FR-016` làm khóa liên kết tới API specification, UI wireframe và test case.

### 1.2. Phạm vi MVP

| Trong phạm vi | Ngoài phạm vi |
| --- | --- |
| Đăng nhập JWT, đổi mật khẩu tạm lần đầu và quản trị cơ bản tài khoản/cán bộ/role | MFA, email kích hoạt, quên mật khẩu tự phục vụ |
| CRUD, tìm kiếm và import Excel hồ sơ hội viên | Membership nâng cao, lịch sử biến động, CCCD, chi hội |
| Văn bản đến, AI gợi ý điều phối có xác nhận của văn thư, nhắc hạn nội bộ | Điều phối tự động không có xác nhận, email/SMS reminder |
| Mẫu văn bản, AI sinh nháp, thẩm định thể thức, phê duyệt, phát hành và lưu trữ | Kiểm tra tính đúng-sai nội dung hoặc pháp lý |
| Attachment PDF/DOCX/XLSX/JPG/JPEG/PNG và tìm kiếm text-based file | OCR ảnh/PDF scan, tìm kiếm nội dung ảnh, Citizen Portal |
| Full-text search bằng PostgreSQL | Search service ngoài, đồng bộ đa hệ thống |

### 1.3. Phụ thuộc hệ thống

| Thành phần | Trách nhiệm trong yêu cầu chức năng |
| --- | --- |
| ASP.NET Core Identity + JWT | Đăng nhập, role, đổi mật khẩu, khóa tài khoản |
| PostgreSQL + EF Core | Lưu dữ liệu nghiệp vụ, history, reminder và full-text index |
| RAG + LLM service (Draft chờ duyệt) | Gợi ý điều phối, sinh nháp, kiểm tra thể thức; xem 02-architecture/03-ai-rag-design.md để biết phạm vi và approval gate |
| File storage | Lưu file đính kèm ngoài database |
| Text extraction worker | Trích xuất text từ PDF có text layer, DOCX và XLSX |
| Reminder worker | Tạo nhắc trước hạn, đến hạn và quá hạn |

## 2. Phân Tích Vai Trò Người Dùng

### 2.1. Vai trò đăng nhập

| Vai trò | Trách nhiệm chính | Quyền thay đổi chính |
| --- | --- | --- |
| `Administrator` | Quản trị tài khoản, cán bộ, role, loại và mẫu văn bản | Tạo/reset/vô hiệu hóa tài khoản; CRUD danh mục và mẫu |
| `Văn thư` | Quản lý vòng đời văn bản đến và phát hành văn bản đi | Tiếp nhận, attachment, xác nhận điều phối, cấp số, lưu trữ |
| `Cán bộ xử lý/soạn thảo` | Xử lý công việc, soạn văn bản, dùng AI và gửi thẩm định | Tạo/sửa văn bản đi do mình soạn; chạy AI/review |
| `Lãnh đạo` | Kiểm soát văn bản trước phát hành | Duyệt hoặc trả văn bản chờ duyệt về chỉnh sửa |

Một `Staff` có thể được gán nhiều role. Không có role `Reviewer` riêng trong MVP: cán bộ soạn kích hoạt thẩm định bằng rule/AI.

### 2.2. Tác nhân hệ thống

| Tác nhân | Trách nhiệm | Không được làm |
| --- | --- | --- |
| `AI Service` | Đưa gợi ý, sinh nháp, phát hiện lỗi thể thức | Tự xác nhận điều phối, tự phê duyệt hoặc ghi đè nội dung đang chỉnh sửa |
| `Reminder Worker` | Tạo thông báo nhắc hạn idempotent | Tự hoàn tất hoặc tự xóa văn bản |
| `Text Extraction Worker` | Trích xuất text nền từ file được hỗ trợ và cập nhật index | OCR ảnh/PDF scan trong MVP |

### 2.3. Nguyên tắc phân quyền và hiển thị

- Mọi `Staff` đang hoạt động được xem, lọc và tìm kiếm toàn bộ văn bản trong MVP.
- Quyền tạo, sửa, điều phối, duyệt và lưu trữ vẫn bắt buộc theo role và trạng thái dữ liệu.
- Cán bộ soạn chỉ sửa văn bản đi do chính mình tạo, trừ khi họ đồng thời có role khác cho phép thao tác đó.
- Thông báo trong `ReminderHistory` chỉ hiển thị cho người nhận; Administrator có thể kiểm tra khi hỗ trợ vận hành.
- `Staff.IsActive = false` chặn đăng nhập và mọi thao tác mới, nhưng không làm mất lịch sử nghiệp vụ.

## 3. Danh Sách Use Case

| Mã | Tên use case | Vai trò chính | Mức ưu tiên | Bảng dữ liệu liên quan |
| --- | --- | --- | --- | --- |
| FR-001 | Đăng nhập, đổi mật khẩu tạm và đăng xuất | Tất cả Staff | P0 | `asp_net_users`, `staff` |
| FR-002 | Quản lý tài khoản, Staff và role | Administrator | P0 | `asp_net_users`, `staff`, Identity roles |
| FR-003 | Xem, tìm kiếm và xem chi tiết hội viên | Administrator, Văn thư | P0 | `members` |
| FR-004 | Tạo, cập nhật và ngừng hoạt động hội viên | Administrator, Văn thư | P0 | `members` |
| FR-005 | Import hội viên từ Excel | Administrator, Văn thư | P0 | `members` |
| FR-006 | Quản lý loại văn bản, mẫu và FormatRules | Administrator | P0 | `document_types`, `document_templates` |
| FR-007 | Tiếp nhận và quản lý văn bản đến | Văn thư | P0 | `incoming_documents`, `document_types` |
| FR-008 | Quản lý file đính kèm và trích xuất text | Văn thư, Cán bộ soạn thảo | P0 | `attachments` |
| FR-009 | AI gợi ý và xác nhận điều phối | Văn thư, AI Service | P0 | `incoming_documents`, `staff` |
| FR-010 | Nhắc hạn tự động và đọc thông báo | Reminder Worker, Staff | P0 | `incoming_documents`, `reminder_history` |
| FR-011 | Tạo văn bản đi theo mẫu | Cán bộ soạn thảo | P0 | `outgoing_documents`, `document_templates`, `members` |
| FR-012 | AI sinh nháp và chỉnh sửa văn bản | Cán bộ soạn thảo, AI Service | P0 | `outgoing_documents` |
| FR-013 | Thẩm định thể thức và xem lịch sử review | Cán bộ soạn thảo, AI Service | P0 | `outgoing_documents`, `review_history` |
| FR-014 | Phê duyệt hoặc trả lại văn bản | Lãnh đạo | P0 | `outgoing_documents`, `review_history` |
| FR-015 | Cấp số, phát hành và lưu trữ | Văn thư | P0 | `outgoing_documents`, `attachments` |
| FR-016 | Tìm kiếm toàn văn văn bản | Tất cả Staff đang hoạt động | P0 | `incoming_documents`, `outgoing_documents`, `attachments` |

## 4. Use Case Chi Tiết

### FR-001 — Đăng nhập, đổi mật khẩu tạm và đăng xuất

| Thuộc tính | Nội dung |
| --- | --- |
| Mục tiêu | Cho phép Staff đang hoạt động truy cập hệ thống an toàn và bắt buộc đổi mật khẩu do Administrator cấp/reset. |
| Vai trò | Tất cả Staff đang hoạt động. |
| Tiền điều kiện | Tài khoản Identity tồn tại, liên kết 1–1 với Staff và Staff đang hoạt động. |
| Hậu điều kiện | Đăng nhập thành công nhận JWT hợp lệ; đổi mật khẩu thành công xóa cờ bắt buộc đổi mật khẩu. |
| Dữ liệu | `asp_net_users`, `staff`, role Identity. |
| Phân quyền | Endpoint đăng nhập là công khai; endpoint đổi mật khẩu yêu cầu phiên hợp lệ; chức năng nghiệp vụ yêu cầu cờ `MustChangePassword = false`. |

**Luồng chính**

1. Người dùng nhập username/email và mật khẩu.
2. Hệ thống xác thực bằng ASP.NET Core Identity và kiểm tra Staff liên kết đang hoạt động.
3. Nếu `MustChangePassword = true`, hệ thống chỉ cho phép người dùng thực hiện đổi mật khẩu hoặc đăng xuất.
4. Người dùng nhập mật khẩu mới hợp lệ; hệ thống cập nhật password hash và đặt `MustChangePassword = false`.
5. Hệ thống cấp JWT đầy đủ quyền theo các role hiện tại.
6. Khi đăng xuất, client xóa JWT và dữ liệu phiên cục bộ.

**Ngoại lệ**

- Sai thông tin đăng nhập hoặc Staff không hoạt động: không cấp token, trả thông báo chung.
- Mật khẩu mới không đạt policy Identity: không đổi password và hiển thị lỗi theo policy.
- Token đã hết hạn: yêu cầu đăng nhập lại.

**Tiêu chí nghiệm thu**

- Tài khoản mới/reset không dùng được màn hình nghiệp vụ trước khi đổi mật khẩu.
- Staff bị vô hiệu hóa không thể đăng nhập dù Identity user vẫn tồn tại.

### FR-002 — Quản lý tài khoản, Staff và role

| Thuộc tính | Nội dung |
| --- | --- |
| Mục tiêu | Administrator quản lý tài khoản nội bộ, hồ sơ cán bộ và phân quyền đa role. |
| Vai trò | Administrator. |
| Tiền điều kiện | Người thao tác có role `Administrator`. |
| Hậu điều kiện | Identity user và Staff được tạo/cập nhật đồng bộ; lịch sử văn bản không bị mất khi vô hiệu hóa. |
| Dữ liệu | `asp_net_users`, `staff`, `asp_net_roles`, `asp_net_user_roles`. |
| Phân quyền | Chỉ Administrator tạo, reset mật khẩu, gán/bỏ role hoặc vô hiệu hóa Staff. |

**Luồng chính**

1. Administrator tạo Staff gồm họ tên, email, bộ phận, chức vụ, username/email đăng nhập và một hay nhiều role.
2. Hệ thống tạo Identity user, password hash từ mật khẩu tạm, `MustChangePassword = true` và tạo Staff liên kết trong cùng transaction.
3. Administrator cập nhật hồ sơ Staff hoặc role; thay đổi role có hiệu lực ở lần cấp JWT tiếp theo.
4. Khi reset mật khẩu, hệ thống đặt mật khẩu tạm mới và bật lại `MustChangePassword`.
5. Khi ngừng sử dụng, Administrator đặt `Staff.IsActive = false` thay vì xóa.

**Ngoại lệ**

- Username/email đã tồn tại hoặc Identity user đã liên kết Staff khác: từ chối tạo.
- Không cho phép xóa Staff đã được tham chiếu bởi văn bản/history.

**Tiêu chí nghiệm thu**

- Một Staff có thể đồng thời mang role Văn thư và Lãnh đạo.
- Reset password buộc người dùng đổi password trước khi thao tác nghiệp vụ tiếp theo.

### FR-003 — Xem, tìm kiếm và xem chi tiết hội viên

| Thuộc tính | Nội dung |
| --- | --- |
| Mục tiêu | Tra cứu nhanh hồ sơ hội viên đã số hóa. |
| Vai trò | Administrator, Văn thư. |
| Tiền điều kiện | Người dùng đăng nhập và Staff đang hoạt động. |
| Hậu điều kiện | Không thay đổi dữ liệu. |
| Dữ liệu | `members`, `outgoing_documents` khi cần xem văn bản liên quan. |
| Phân quyền | Chỉ Administrator và Văn thư được xem danh sách/chi tiết hội viên trong MVP. |

**Luồng chính**

1. Người dùng mở danh sách hội viên có phân trang.
2. Người dùng tìm theo họ tên, điện thoại hoặc email; có thể lọc `Active`/`Inactive`.
3. Hệ thống trả danh sách phù hợp và cho phép mở chi tiết một hội viên.
4. Chi tiết hiển thị thông tin hồ sơ và danh sách văn bản đi liên kết, nếu có.

**Ngoại lệ**

- Không có kết quả: trả danh sách rỗng, không báo lỗi hệ thống.
- Id hội viên không tồn tại: trả `NotFound`.

**Tiêu chí nghiệm thu**

- Tìm theo một trong ba trường hỗ trợ kết quả phân trang và giữ được bộ lọc.
- Hội viên Inactive vẫn xem được để tra cứu lịch sử.

### FR-004 — Tạo, cập nhật và ngừng hoạt động hội viên

| Thuộc tính | Nội dung |
| --- | --- |
| Mục tiêu | Duy trì hồ sơ hội viên chuẩn hóa, không xóa mất lịch sử. |
| Vai trò | Administrator, Văn thư. |
| Tiền điều kiện | Người dùng có role hợp lệ. |
| Hậu điều kiện | Hồ sơ được tạo/cập nhật hoặc chuyển `Inactive`; `UpdatedAt` được cập nhật. |
| Dữ liệu | `members`. |
| Phân quyền | Chỉ Administrator và Văn thư thay đổi hồ sơ. |

**Luồng chính**

1. Người dùng nhập hoặc chỉnh sửa các trường trong schema `members`.
2. Hệ thống kiểm tra `FullName` bắt buộc, ngày tháng hợp lệ, format email/điện thoại nếu có.
3. Hệ thống lưu hồ sơ mới với `Status = Active` mặc định hoặc cập nhật dữ liệu hiện có.
4. Khi hội viên không còn hoạt động, người dùng chọn ngừng hoạt động; hệ thống chuyển `Status = Inactive`.

**Ngoại lệ**

- Không cho xóa cứng hội viên.
- Dữ liệu không hợp lệ: trả lỗi theo trường, không lưu một phần.

**Tiêu chí nghiệm thu**

- Chuyển Inactive không làm mất liên kết `RelatedMemberId` của văn bản đi cũ.
- Hội viên Inactive không xuất hiện trong bộ chọn hội viên khi tạo văn bản mới.

### FR-005 — Import hội viên từ Excel

| Thuộc tính | Nội dung |
| --- | --- |
| Mục tiêu | Nhập nhanh dữ liệu hội viên có sẵn theo file chuẩn. |
| Vai trò | Administrator, Văn thư. |
| Tiền điều kiện | Người dùng tải file `.xlsx` theo template được hệ thống cung cấp. |
| Hậu điều kiện | Toàn bộ dữ liệu hợp lệ được import, hoặc không có dòng nào được ghi. |
| Dữ liệu | `members`. |
| Phân quyền | Chỉ Administrator và Văn thư import/tải template. |

**Luồng chính**

1. Người dùng tải template Excel chứa đúng các cột của `members`.
2. Người dùng chọn file `.xlsx` để import.
3. Hệ thống validate toàn bộ file: cột bắt buộc, kiểu ngày, email/điện thoại, giá trị status và dữ liệu trùng.
4. Nếu tất cả dòng hợp lệ, hệ thống ghi toàn bộ trong một transaction và trả tổng số đã import.

**Ngoại lệ**

- File sai định dạng/cột: không import, trả lỗi cấu trúc file.
- Có dòng lỗi: không import dòng nào; trả báo cáo số dòng, tên cột và nguyên nhân.
- Trùng chính xác bộ `FullName + DateOfBirth + Phone` sau chuẩn hóa trong file hoặc với database: coi là lỗi import.

**Tiêu chí nghiệm thu**

- Một dòng lỗi khiến toàn bộ transaction rollback.
- Trường Status để trống được gán `Active`; các giá trị khác `Active`/`Inactive` bị từ chối.

### FR-006 — Quản lý loại văn bản, mẫu và FormatRules

| Thuộc tính | Nội dung |
| --- | --- |
| Mục tiêu | Cho phép Administrator chuẩn bị danh mục và template dùng cho soạn thảo/thẩm định. |
| Vai trò | Administrator. |
| Tiền điều kiện | Administrator đăng nhập. |
| Hậu điều kiện | Type/template được tạo, cập nhật hoặc vô hiệu hóa; dữ liệu lịch sử được giữ nguyên. |
| Dữ liệu | `document_types`, `document_templates`. |
| Phân quyền | Chỉ Administrator CRUD danh mục và mẫu. |

**Luồng chính**

1. Administrator tạo loại văn bản với `Code` unique và tên hiển thị.
2. Administrator tạo mẫu thuộc một loại đang hoạt động, nhập `TemplateContent` và `FormatRules` JSON.
3. Hệ thống validate `FormatRules` là JSON object có `version` và danh sách `rules` hợp lệ.
4. Administrator sửa nội dung/quy tắc hoặc chuyển `IsActive = false` khi ngừng sử dụng.

**Ngoại lệ**

- Code type trùng hoặc tên mẫu trùng trong cùng loại: từ chối lưu.
- JSON không hợp lệ: trả lỗi theo vị trí/nội dung JSON.
- Type/template đã được tham chiếu không được xóa cứng.

**Tiêu chí nghiệm thu**

- Chỉ template active xuất hiện khi tạo văn bản đi mới.
- Sửa template không làm thay đổi Content của văn bản đã khởi tạo.

### FR-007 — Tiếp nhận và quản lý văn bản đến

| Thuộc tính | Nội dung |
| --- | --- |
| Mục tiêu | Số hóa/vào sổ văn bản đến có hạn xử lý. |
| Vai trò | Văn thư. |
| Tiền điều kiện | Có document type đang hoạt động. |
| Hậu điều kiện | Tạo/cập nhật `incoming_documents` với trạng thái hợp lệ. |
| Dữ liệu | `incoming_documents`, `document_types`, `attachments`. |
| Phân quyền | Chỉ Văn thư tạo/sửa văn bản đến; mọi Staff active được xem. |

**Luồng chính**

1. Văn thư nhập số hiệu, nơi gửi, trích yếu, ngày nhận, deadline và loại văn bản.
2. Hệ thống kiểm tra `ReceivedDate <= Deadline`, tạo văn bản với `Status = New`.
3. Văn thư có thể thêm attachment theo FR-008.
4. Văn thư sửa thông tin hành chính khi văn bản chưa Completed.
5. Từ chi tiết văn bản đến, Văn thư có thể bắt đầu FR-009 hoặc tạo văn bản trả lời theo FR-011.

**Ngoại lệ**

- Deadline trước ngày nhận hoặc type inactive: từ chối lưu.
- Không cho sửa dữ liệu hành chính sau khi văn bản Completed.

**Tiêu chí nghiệm thu**

- Văn bản mới luôn có `New`, chưa có người xử lý và chưa có reminder.
- Văn bản đến có thể được liên kết ngược với một hoặc nhiều văn bản đi trả lời.

### FR-008 — Quản lý file đính kèm và trích xuất text

| Thuộc tính | Nội dung |
| --- | --- |
| Mục tiêu | Upload, truy xuất và quản lý an toàn attachment của văn bản. |
| Vai trò | Văn thư, Cán bộ soạn thảo; mọi Staff active tải xuống. |
| Tiền điều kiện | Tài liệu cha tồn tại; người upload có quyền sửa tài liệu cha. |
| Hậu điều kiện | File được lưu ngoài database, metadata được tạo và extraction được xếp hàng nếu phù hợp. |
| Dữ liệu | `attachments`, `incoming_documents`, `outgoing_documents`. |
| Phân quyền | Văn thư thao tác attachment văn bản đến; người soạn thao tác attachment văn bản đi do mình soạn; mọi Staff active được tải xuống. |

**Luồng chính**

1. Người dùng chọn một file PDF, DOCX, XLSX, JPG, JPEG hoặc PNG.
2. Hệ thống kiểm tra loại, dung lượng theo cấu hình và quyền với tài liệu cha.
3. Storage service lưu file với object key/path an toàn; database tạo đúng một liên kết cha trong `attachments`.
4. Với PDF/DOCX/XLSX, hệ thống đặt `ExtractionStatus = Pending` và Text Extraction Worker xử lý nền.
5. Với ảnh, hệ thống đặt `ExtractionStatus = Unsupported`; upload vẫn thành công.
6. Người có quyền tải file qua API đã kiểm tra quyền truy cập tài liệu cha.

**Ngoại lệ**

- File sai loại hoặc vượt dung lượng: không ghi metadata, không lưu file.
- Trích xuất lỗi: attachment vẫn tồn tại, trạng thái `Failed`, có `ExtractionError` và không chặn nghiệp vụ.
- Văn bản Archived hoặc Completed: không cho thêm/xóa attachment, vẫn cho phép tải xuống.

**Tiêu chí nghiệm thu**

- Attachment không thể đồng thời gắn vào incoming và outgoing document.
- DOCX/PDF có text/XLSX sau khi trích xuất thành công có thể tham gia FR-016; ảnh không OCR trong MVP.

### FR-009 — AI gợi ý và xác nhận điều phối

| Thuộc tính | Nội dung |
| --- | --- |
| Mục tiêu | Giúp Văn thư chọn người xử lý phù hợp nhưng luôn cần quyết định của con người. |
| Vai trò | Văn thư, AI Service. |
| Tiền điều kiện | Văn bản đến chưa Completed; có danh sách Staff active. |
| Hậu điều kiện | Gợi ý mới nhất được lưu; khi xác nhận, văn bản có người xử lý và chuyển InProgress. |
| Dữ liệu | `incoming_documents`, `staff`. |
| Phân quyền | Chỉ Văn thư chạy gợi ý, chọn người xử lý và xác nhận điều phối. |

**Luồng chính**

1. Văn thư yêu cầu AI phân tích trích yếu và loại văn bản.
2. AI trả về Staff gợi ý, lý do và confidence nếu có.
3. Hệ thống lưu bộ gợi ý mới nhất vào các trường `Suggested*`/`Assignment*` tương ứng.
4. Văn thư chọn Staff active; có thể chọn khác gợi ý AI.
5. Hệ thống ghi người được giao, người xác nhận, thời điểm xác nhận và chuyển `New` sang `InProgress`.
6. Trước khi Completed, Văn thư có thể giao lại; MVP chỉ giữ thông tin điều phối cuối cùng.

**Ngoại lệ**

- AI lỗi/timeout: không thay đổi assignment hay status; Văn thư vẫn xác nhận thủ công.
- Staff đã inactive: không cho chọn làm người xử lý.

**Tiêu chí nghiệm thu**

- AI không thể tự chuyển status hoặc tự gán người xử lý.
- Chạy lại AI chỉ thay bộ gợi ý mới nhất, không ghi đè xác nhận điều phối.

### FR-010 — Nhắc hạn tự động và đọc thông báo

| Thuộc tính | Nội dung |
| --- | --- |
| Mục tiêu | Nhắc cán bộ được giao trước hạn, đến hạn và khi quá hạn xử lý. |
| Vai trò | Reminder Worker, Staff được giao. |
| Tiền điều kiện | Văn bản đến có `AssignedToStaffId`, chưa Completed. |
| Hậu điều kiện | Tạo/đọc `reminder_history` idempotent; status được cập nhật Overdue khi cần. |
| Dữ liệu | `incoming_documents`, `reminder_history`. |
| Phân quyền | Worker tạo reminder; người nhận xem và đánh dấu read reminder của mình. |

**Luồng chính**

1. Worker chạy theo lịch cấu hình và tạo scope database riêng.
2. Worker tìm văn bản đang mở có người xử lý.
3. Worker tạo `BeforeDeadline`, `DueDate` hoặc `Overdue` theo ngày nghiệp vụ.
4. Khi deadline đã qua và chưa Completed, worker chuyển status sang `Overdue`.
5. Staff mở danh sách thông báo của mình và đánh dấu đã đọc; hệ thống ghi `ReadAt`.

**Ngoại lệ**

- Job chạy lại cùng ngày: unique key ngăn tạo reminder trùng.
- Worker lỗi: ghi log vận hành; lần chạy sau được phép thử lại.

**Tiêu chí nghiệm thu**

- Một reminder chỉ tồn tại một lần theo document, recipient, kind và reminder date.
- Văn bản Completed không nhận reminder mới.

### FR-011 — Tạo văn bản đi theo mẫu

| Thuộc tính | Nội dung |
| --- | --- |
| Mục tiêu | Khởi tạo văn bản đi có nội dung chuẩn, có thể gắn văn bản đến và một hội viên. |
| Vai trò | Cán bộ xử lý/soạn thảo. |
| Tiền điều kiện | Có template active; người dùng có role soạn thảo. |
| Hậu điều kiện | Tạo `outgoing_documents` ở trạng thái Editing và người tạo là DraftedByStaff. |
| Dữ liệu | `outgoing_documents`, `document_templates`, `members`, `incoming_documents`. |
| Phân quyền | Chỉ role soạn thảo tạo; chỉ người soạn sửa tiếp ở trạng thái cho phép. |

**Luồng chính**

1. Người soạn chọn template active.
2. Người soạn có thể chọn một incoming document để trả lời và/hoặc một Member active liên quan.
3. Hệ thống thay placeholder trong TemplateContent bằng dữ liệu sẵn có.
4. Hệ thống tạo văn bản với Title và Content khởi tạo, `DraftedByStaffId` là người tạo, `Status = Editing`.
5. Người soạn tiếp tục chỉnh thủ công, thêm attachment hoặc dùng FR-012.

**Ngoại lệ**

- Template/member inactive hoặc id liên quan không tồn tại: không cho tạo.
- Không cho liên kết quá một Member trực tiếp trong MVP.

**Tiêu chí nghiệm thu**

- Văn bản tạo từ incoming document giữ `RelatedIncomingDocumentId` để tra cứu hai chiều.
- Content là bản độc lập, không thay đổi khi template hoặc Member bị cập nhật sau đó.

### FR-012 — AI sinh nháp và chỉnh sửa văn bản

| Thuộc tính | Nội dung |
| --- | --- |
| Mục tiêu | Hỗ trợ sinh bản nháp AI nhưng giữ toàn quyền chỉnh sửa cho cán bộ. |
| Vai trò | Cán bộ soạn thảo, AI Service. |
| Tiền điều kiện | Người dùng là DraftedByStaff, văn bản ở AiDraft/Editing/ReviewFailed. |
| Hậu điều kiện | Lần AI đầu tiên được bảo tồn tại AiDraftContent; Content là nội dung đang làm việc. |
| Dữ liệu | `outgoing_documents`. |
| Phân quyền | Chỉ người soạn thao tác AI hoặc sửa Content của văn bản mình. |

**Luồng chính**

1. Người soạn gửi context của template, Member và incoming document liên quan cho AI.
2. AI trả bản nháp để người dùng xem trước.
3. Khi người dùng chấp nhận bản AI đầu tiên, hệ thống ghi cùng nội dung vào `AiDraftContent` và `Content`, chuyển trạng thái `AiDraft`.
4. Khi bắt đầu chỉnh sửa, trạng thái chuyển `Editing`; các lần lưu sau chỉ cập nhật `Content`.
5. Người dùng có thể chạy AI lại; kết quả mới chỉ cập nhật Content khi được chấp nhận và không được ghi đè AiDraftContent.

**Ngoại lệ**

- AI lỗi/timeout: không thay đổi Content, AiDraftContent hay status; thông báo cho người dùng thử lại hoặc sửa thủ công.
- Văn bản PendingReview/PendingApproval/Approved/Archived: không cho sinh AI hoặc chỉnh Content.

**Tiêu chí nghiệm thu**

- So sánh giữa AiDraftContent và Content luôn hiển thị được sau nhiều lần chỉnh sửa.
- Lỗi AI không làm mất nội dung chưa lưu của người dùng trên UI hoặc nội dung đã lưu trong database.

### FR-013 — Thẩm định thể thức và xem lịch sử review

| Thuộc tính | Nội dung |
| --- | --- |
| Mục tiêu | Kiểm tra thể thức qua rule/AI và lưu vết rõ từng vòng sửa. |
| Vai trò | Cán bộ soạn thảo, AI Service. |
| Tiền điều kiện | Người dùng là DraftedByStaff; văn bản ở Editing hoặc ReviewFailed; template có FormatRules. |
| Hậu điều kiện | Tạo một ReviewHistory immutable và cập nhật trạng thái/lỗi gần nhất của văn bản. |
| Dữ liệu | `outgoing_documents`, `document_templates`, `review_history`. |
| Phân quyền | Người soạn gửi review; mọi Staff active được xem lịch sử vì có quyền xem toàn bộ văn bản. |

**Luồng chính**

1. Người soạn gửi văn bản thẩm định; hệ thống chuyển yêu cầu sang service rule/AI.
2. Service lấy FormatRules của template và kiểm tra thể thức, không kết luận nội dung/pháp lý.
3. Trong cùng transaction, hệ thống tạo AttemptNo kế tiếp, lưu ContentSnapshot, ReviewResult và ReviewIssues.
4. Hệ thống cập nhật `OutgoingDocuments.ReviewIssues` bằng lỗi mới nhất.
5. Nếu Failed, status chuyển `ReviewFailed`; nếu Passed, status chuyển `PendingApproval`.
6. Người dùng xem danh sách lịch sử để so sánh snapshot/lỗi giữa các attempt.

**Ngoại lệ**

- AI/rule service lỗi trước khi có kết quả: không tạo ReviewHistory, giữ trạng thái Editing/ReviewFailed và cho phép thử lại.
- Văn bản không ở trạng thái hợp lệ: từ chối gửi review.

**Tiêu chí nghiệm thu**

- Mỗi review thành công thêm đúng một dòng, AttemptNo tăng tuần tự.
- Một review Passed không có issue severity Error và chuyển đúng PendingApproval.

### FR-014 — Phê duyệt hoặc trả lại văn bản

| Thuộc tính | Nội dung |
| --- | --- |
| Mục tiêu | Đảm bảo chỉ văn bản đạt thẩm định được lãnh đạo phê duyệt. |
| Vai trò | Lãnh đạo. |
| Tiền điều kiện | Văn bản ở PendingApproval và ReviewHistory gần nhất là Passed. |
| Hậu điều kiện | Văn bản chuyển Approved hoặc trở lại Editing. |
| Dữ liệu | `outgoing_documents`, `review_history`. |
| Phân quyền | Chỉ role Lãnh đạo duyệt/trả lại; Administrator không mặc nhiên có quyền này nếu không mang role Lãnh đạo. |

**Luồng chính**

1. Lãnh đạo mở danh sách văn bản PendingApproval.
2. Hệ thống hiển thị Content, AiDraftContent, lỗi review gần nhất và lịch sử review.
3. Lãnh đạo chọn phê duyệt; hệ thống ghi ApprovedByStaffId, ApprovedAt và chuyển Approved.
4. Hoặc Lãnh đạo trả lại chỉnh sửa; hệ thống chuyển status về Editing và xóa dữ liệu duyệt nếu có.
5. Văn bản trả lại phải qua FR-013 lại trước khi có thể trình duyệt.

**Ngoại lệ**

- Latest review không Passed hoặc status không PendingApproval: từ chối phê duyệt.
- Lãnh đạo inactive/không có role: từ chối thao tác.

**Tiêu chí nghiệm thu**

- Không thể phê duyệt trực tiếp từ Editing, ReviewFailed hoặc PendingReview.
- Sau khi trả lại, người soạn sửa và review đạt mới có thể trình duyệt lại.

### FR-015 — Cấp số, phát hành và lưu trữ

| Thuộc tính | Nội dung |
| --- | --- |
| Mục tiêu | Hoàn tất vòng đời văn bản đã được duyệt bằng số/ký hiệu và lưu trữ bất biến. |
| Vai trò | Văn thư. |
| Tiền điều kiện | Văn bản có status Approved, ApprovedByStaffId và ApprovedAt. |
| Hậu điều kiện | Có ReferenceNumber, IssuedDate, ArchivedAt; status chuyển Archived. |
| Dữ liệu | `outgoing_documents`, `attachments`. |
| Phân quyền | Chỉ Văn thư phát hành/lưu trữ; Lãnh đạo chỉ duyệt. |

**Luồng chính**

1. Văn thư chọn một văn bản Approved.
2. Văn thư nhập số/ký hiệu và ngày phát hành.
3. Hệ thống kiểm tra số/ký hiệu không trùng và dữ liệu duyệt còn hợp lệ.
4. Hệ thống ghi ReferenceNumber, IssuedDate, ArchivedAt và chuyển Archived trong cùng transaction.
5. Văn bản và attachment chỉ còn cho phép xem/tải xuống.

**Ngoại lệ**

- Thiếu số, thiếu ngày phát hành hoặc số trùng: từ chối lưu trữ.
- Văn bản chưa Approved hoặc đã Archived: không cho thao tác.

**Tiêu chí nghiệm thu**

- Chỉ Văn thư có role hợp lệ mới cấp số/lưu trữ được.
- Archived là trạng thái cuối, không thể sửa Content hoặc attachment.

### FR-016 — Tìm kiếm toàn văn văn bản

| Thuộc tính | Nội dung |
| --- | --- |
| Mục tiêu | Cho phép Staff tra cứu nhanh nội dung văn bản và file có text trích xuất. |
| Vai trò | Tất cả Staff đang hoạt động. |
| Tiền điều kiện | Staff đăng nhập, không bị vô hiệu hóa; full-text index và extraction worker hoạt động. |
| Hậu điều kiện | Không thay đổi dữ liệu; trả danh sách kết quả phân trang. |
| Dữ liệu | `incoming_documents`, `outgoing_documents`, `attachments` và GIN index. |
| Phân quyền | Mọi Staff active được tìm và xem toàn bộ kết quả văn bản trong MVP. |

**Luồng chính**

1. Người dùng nhập từ khóa tối thiểu 2 ký tự không phải khoảng trắng.
2. Người dùng có thể lọc theo chiều văn bản, loại văn bản, trạng thái, khoảng ngày và nguồn khớp.
3. Hệ thống tìm trong Summary của văn bản đến; Title, Content, AiDraftContent của văn bản đi; ExtractedText của attachment.
4. Hệ thống trả loại tài liệu, document id, tiêu đề/trích yếu, MatchSource, snippet bôi nổi bật và score.
5. Người dùng mở chi tiết tài liệu từ kết quả.

**Ngoại lệ**

- Attachment Pending/Processing/Failed/Unsupported chưa đóng góp nội dung tìm kiếm; upload vẫn không bị lỗi.
- PDF scan và ảnh có status Unsupported trong MVP vì không có OCR.
- Từ khóa ngắn hơn 2 ký tự: yêu cầu nhập thêm, không chạy query toàn văn.

**Tiêu chí nghiệm thu**

- Text trong DOCX, XLSX hoặc PDF có text layer được tìm thấy sau khi status extraction là Succeeded.
- Kết quả cho biết nguồn khớp là Summary, Content, AiDraftContent hoặc Attachment.

## 5. Business Rules Tổng Hợp

### 5.1. Authentication và phân quyền

1. Identity user phải có đúng một Staff liên kết; một Staff chỉ liên kết một Identity user.
2. Staff inactive không đăng nhập và không thao tác mới được.
3. Tạo/reset password luôn đặt `MustChangePassword = true`; chỉ đổi password thành công mới đặt về false.
4. Trong thời gian bắt buộc đổi password, chỉ cho phép đổi password hoặc đăng xuất.
5. Một Staff có thể có nhiều role; quyền nhạy cảm yêu cầu đúng role, không suy diễn từ chức danh.
6. Mọi Staff active xem được toàn bộ văn bản; quyền xem reminder chỉ thuộc người nhận hoặc Administrator hỗ trợ vận hành.

### 5.2. Hội viên và import

1. Members không xóa cứng; chỉ Active/Inactive.
2. FullName bắt buộc; date, email, phone phải hợp lệ khi được nhập.
3. Import chỉ nhận `.xlsx` theo template; validate toàn file và transaction all-or-nothing.
4. Dòng trùng chính xác `FullName + DateOfBirth + Phone` sau chuẩn hóa là lỗi import.
5. Membership nâng cao, CCCD, chi hội và lịch sử biến động ngoài MVP.

### 5.3. Văn bản, trạng thái và attachment

1. Incoming document: `New → InProgress → Completed`, hoặc `New/InProgress → Overdue → Completed`.
2. AI chỉ gợi ý; Văn thư luôn là người xác nhận/giao lại người xử lý.
3. Outgoing document: `AiDraft/Editing → PendingReview → ReviewFailed hoặc PendingApproval → Approved → Archived`.
4. Trả từ PendingApproval về Editing phải review lại trước khi phê duyệt.
5. Chỉ Lãnh đạo phê duyệt; chỉ Văn thư cấp số/ngày phát hành và lưu trữ.
6. Archived là trạng thái cuối, khóa Content và attachment.
7. Attachment thuộc đúng một tài liệu; chỉ nhận PDF/DOCX/XLSX/JPG/JPEG/PNG, dung lượng do cấu hình hệ thống quyết định.

### 5.4. AI, review và reminder

1. Lỗi/timeout AI không ghi đè Content, AiDraftContent, assignment hoặc status hiện tại.
2. AiDraftContent chỉ lưu bản AI đầu tiên; Content là bản cán bộ có thể tiếp tục chỉnh.
3. Mỗi review thành công tạo một ReviewHistory immutable với snapshot, AttemptNo, result và issues.
4. Thẩm định chỉ kiểm tra thể thức/hình thức; không xác nhận nội dung hoặc căn cứ pháp lý đúng-sai.
5. Reminder chỉ được tạo cho incoming document đã giao người và chưa Completed.
6. Unique key reminder bảo đảm job chạy lại không tạo trùng; overdue reminder có thể có một bản ghi mỗi ngày.

### 5.5. Text extraction và full-text search

1. Text Extraction Worker xử lý nền PDF có text layer, DOCX và XLSX; không OCR ảnh/PDF scan.
2. Extraction status gồm `Pending`, `Processing`, `Succeeded`, `Failed`, `Unsupported`.
3. Extraction lỗi hoặc không hỗ trợ không chặn upload/download; chỉ khiến attachment không xuất hiện khi tìm theo nội dung file.
4. Full-text search dùng PostgreSQL, trả kết quả phân trang theo score và phải thể hiện MatchSource/snippet.
5. API search không trả raw ExtractedText mặc định; chỉ trả snippet và chỉ tải file qua endpoint đã kiểm tra quyền.
