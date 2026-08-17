# Hướng dẫn Cài đặt & Chạy RAG Data Scraper Web Dashboard

Tool cào dữ liệu và trích xuất văn bản thành **staging package** phục vụ kho tham
chiếu pháp luật/hướng dẫn của DigitalOps. Crawl hoặc RAG Health thành công không
đồng nghĩa nguồn đã được duyệt để publish vào Qdrant.

---

## 1. Yêu cầu môi trường

- **Python**: 3.11+ (khuyến nghị Python 3.11, 3.12 hoặc 3.13).
- **Tesseract OCR** (cần cho PDF dạng file quét/image): tool tìm executable
  trong `PATH` và thư mục cài đặt Windows thông dụng. Model ngôn ngữ runtime
  nằm tại `storage/ocr/tessdata`; cấu hình bằng `ocr.tessdata_dir`.
- **LibreOffice** (cần khi đọc file `.doc`/RTF cũ): tool tìm `soffice` trong
  `PATH`, thư mục cài đặt Windows thông dụng hoặc biến môi trường
  `RAG_SCRAPER_LIBREOFFICE`. Chuyển đổi chạy headless trong thư mục tạm, có
  timeout và giới hạn kích thước output.

---

## 2. Cài đặt Python Virtual Environment (Chỉ làm 1 lần đầu)

Tại thư mục `tools/rag-data-scraper`:

```powershell
python -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install -e .
```

Khởi tạo Cơ sở dữ liệu SQLite theo dõi cào:
```powershell
python -m rag_data_scraper.cli init-db
```

---

## 3. Chạy Tool Cào Dữ Liệu bằng Trang Web (Giao diện Trực Quan 1-Click - KHUYÊN DÙNG)

Bạn chỉ cần chạy 1 câu lệnh (hoặc nhấp đúp file `run_web.bat`):

```powershell
python -m rag_data_scraper.cli web --open
```

Trình duyệt sẽ tự động mở trang web quản trị tại: `http://localhost:8000`

### Thao tác 1-Click trên Trang Web:
1. **Chọn Nguồn Cào**: Chọn `vanban.chinhphu.vn`, `thuvienphapluat.vn` hoặc `Generic Web` (cho URL trang web tùy chỉnh).
2. **Nhập URL & Giới hạn (`limit`)**: Dán một hoặc nhiều URL và chọn số văn bản chính tối đa cần tạo (vd: 10, 50, 100). Trang danh sách và lượt tải attachment không tiêu tốn limit này.
3. **Kiểm tra URL trước khi cào**: Bấm **"Kiểm tra URL"** để đọc tối đa 10 seed URL và 100 trang phân trang. Kết quả cho biết số trang danh sách, số văn bản có thể quét và số tệp đính kèm nhưng không tạo job, không tải tệp và không ghi raw/checkpoint/SQLite. Nguồn có parser danh sách riêng được đếm theo record; website chung chỉ là ước lượng từ liên kết HTML và được ghi nhãn rõ trên giao diện.
4. **Giới hạn phân trang**: Chọn số URL trang danh sách tối đa mà crawler được phép lần theo. Chỉ cần nhập trang đầu; các liên kết `page=2`, `page=3`, `/page/2`... được tự phát hiện và đi tiếp theo chuỗi, kể cả khi đã đủ số văn bản đầu ra, để dashboard báo đúng tổng record phát hiện.
5. **Chọn định dạng đầu ra**: Chọn file cần tạo ngay sau khi cào. `Chunks JSONL` là mặc định khuyên dùng cho RAG; 12 định dạng còn lại vẫn có thể chọn trước hoặc xuất lại sau.
6. **Tệp đính kèm**: Bật/tắt việc đưa liên kết PDF, DOCX và `.doc`/RTF cũ vào hàng đợi cào. `.doc` được chuyển đổi bằng LibreOffice headless; nếu runtime thiếu hoặc file lỗi, record metadata/trích yếu từ trang danh sách vẫn được giữ lại cho RAG.
7. **Bắt Đầu Cào (1-Click)**: Bấm nút **"Bắt đầu cào dữ liệu"**. Trang web sẽ tự động chạy ngầm, cập nhật tiến trình và tạo file đã chọn khi job hoàn tất.
8. **Mở RAG Inspector**: Nhấp **"Xem Preview"** để kiểm tra tổng quan, văn bản, chunks, RAG Health và metadata kỹ thuật.
9. **Xuất định dạng khác**: Chọn định dạng ở cột hành động rồi bấm **"Xuất"**. Định dạng đã chọn trước khi cào được chọn sẵn và hiển thị trạng thái file sẵn sàng.

API thăm dò tương ứng nhận HTTPS URL và không tạo trạng thái crawler:

```text
POST /api/url-probes
```

---

## 4. Chạy qua dòng lệnh CLI (Tùy chọn nâng cao)

Nếu không dùng trang web, bạn vẫn có thể chạy qua dòng lệnh CLI:

Registry quản trị nguồn mặc định nằm tại `config/source-registry.json`. Adapter
và toàn bộ host seed phải khớp cùng một entry để package nhận
`corpus_type=legal_reference` cùng provenance đã đăng ký. Generic Web ngoài
registry vẫn crawl được ở corpus `general/unverified`, nhưng không đủ điều kiện
admission vào kho tham chiếu pháp luật. Nguồn tổng hợp được gắn
`cross_check_only`, chỉ phục vụ discovery/đối chiếu.

### 4.1. Cào từ Cổng thông tin Chính phủ (`vanban.chinhphu.vn`)
```powershell
python -m rag_data_scraper.cli crawl --source gov_portal --job-id JOB_GOV_01 --urls https://vanban.chinhphu.vn
```

Giới hạn chuỗi phân trang ở 25 trang (mặc định):

```powershell
python -m rag_data_scraper.cli crawl --source generic_web --job-id JOB_LIST_01 --urls https://example.org/documents --limit 200 --pagination-limit 25
```

Tạo sẵn PDF sau khi cào và bỏ qua tệp đính kèm:

```powershell
python -m rag_data_scraper.cli crawl --source gov_portal --job-id JOB_GOV_PDF --urls https://vanban.chinhphu.vn --export-format documents_pdf --no-attachments
```

### 4.2. Cào từ Thư Viện Pháp Luật (`thuvienphapluat.vn`)
```powershell
python -m rag_data_scraper.cli crawl --source legal_aggregator --job-id JOB_LEGAL_01 --urls https://thuvienphapluat.vn
```

### 4.3. Mở Bảng Báo Cáo HTML Preview
```powershell
python -m rag_data_scraper.cli preview --job-id JOB_GOV_01 --open
```

---

## 5. Đầu ra Staging Package & Preview

Dữ liệu sau khi cào, trích xuất và chunking sẽ được tự động xuất ra tại:
`storage/staging/<job_id>/`

Bao gồm:
- 🌐 `preview.html`: **RAG Inspector tự chứa**, có tìm kiếm, lọc, phân trang và drawer chi tiết.
- 📁 `artifacts/`: bản raw và normalized tự chứa, được kiểm tra SHA-256.
- 📄 `manifest.json`
- 📄 `document-observations.jsonl`
- 📄 `chunk-sets.jsonl`
- 📄 `chunks.jsonl`
- 📁 `exports/`: file định dạng được chọn trước khi cào và file `.sha256` tương ứng.
- 📄 `job-metadata.json`: nguồn, lựa chọn tệp đính kèm và trạng thái auto-export để dashboard khôi phục sau khi restart.


### 5.1. Các định dạng xuất cho RAG

Dashboard tự tạo định dạng đã chọn trong `exports/` khi job hoàn tất. Các lần xuất định dạng khác được tạo theo yêu cầu; những file canonical của package staging gốc không bị sửa:

| Định dạng | Dùng khi nào | Đặc điểm |
| --- | --- | --- |
| `chunks_jsonl` | Khuyên dùng cho vector pipeline, embedding hoặc ETL tự động | Mỗi dòng là một chunk gồm `id`, `text` nguyên bản và `metadata` có source, hash, offset, ACL |
| `staging_zip` | Nạp bằng `DigitalOps.RagIngestion` hoặc lưu bản lossless | Chứa manifest, observations, chunk-sets, chunks, errors và toàn bộ artifacts |
| `chunks_csv` | Kiểm tra thủ công, Excel hoặc pipeline dữ liệu phẳng | UTF-8 BOM; trường có nguy cơ trở thành công thức spreadsheet được thêm dấu `'` ở đầu |
| `documents_markdown_zip` | Công cụ RAG nhập theo từng tài liệu Markdown | Mỗi observation là một file `.md` có YAML-compatible front matter và normalized text |
| `documents_html` | Trình duyệt, kho tài liệu HTML hoặc bộ nạp web | Một HTML độc lập; nội dung nguồn được escape và không tải script/tài nguyên ngoài |
| `documents_pdf` | Chia sẻ, lưu trữ hoặc bộ nạp PDF | PDF phân trang, font Unicode và metadata nguồn |
| `documents_docx` | Microsoft Word hoặc bộ nạp DOCX | Một tài liệu có metadata và normalized text đầy đủ |
| `documents_txt_zip` | Bộ nạp plain text theo từng tài liệu | Mỗi observation là một file UTF-8 `.txt` nguyên bản, kèm manifest JSON |
| `chunks_xlsx` | Review bằng Excel hoặc ETL bảng tính | Hai sheet `Chunks`/`Documents`; chống formula injection và tự chia ô text dài |
| `chunks_json` | API hoặc pipeline cần một JSON hợp lệ duy nhất | Envelope JSON chứa mảng chunk và metadata giống JSONL |
| `documents_pptx` | Review/trình bày hoặc bộ nạp PowerPoint | Text được chia thành slide; metadata nguồn nằm trong notes |
| `documents_xml` | Tích hợp hệ thống XML | Cây document/chunk; ký tự XML không hợp lệ được thay bằng U+FFFD |
| `documents_svg_zip` | Kho đồ họa hoặc bộ nạp SVG | Một SVG an toàn mỗi observation; full normalized text nằm trong `metadata` |

Các URL API tương ứng:

```text
GET /api/jobs/<job_id>/exports
GET /api/jobs/<job_id>/exports/<format>
```

Ví dụ tải JSONL trực tiếp:

```powershell
Invoke-WebRequest `
  -Uri "http://127.0.0.1:8000/api/jobs/JOB_GOV_01/exports/chunks_jsonl" `
  -OutFile "JOB_GOV_01-chunks.jsonl"
```

Trước khi tạo file, server kiểm tra manifest, SHA-256 của artifacts, quan hệ
observation/chunk-set/chunk, offset, content hash và ACL. Package thiếu hoặc bị
sửa sai trả `409`; export vượt giới hạn 1 GiB trả `413`; writer bị thiếu trả
`503` mà không lộ chi tiết dependency. File xuất theo yêu cầu được xóa sau khi
response tải xuống kết thúc; file đã chọn trước khi cào được giữ trong `exports/`
và kiểm tra bằng SHA-256 trước khi tái sử dụng. PPTX giới hạn 2.000 slide; DOCX/PPTX giới hạn 10.000 document
để tránh tạo output ngoài kiểm soát.

### 5.2. Độ bền và khả năng tiếp tục của crawler

- Một HTTP client được tái sử dụng trong toàn job, có keep-alive, giới hạn đồng thời theo host và khoảng nghỉ giữa các request.
- Các lỗi tạm thời `408`, `425`, `429`, `500`, `502`, `503`, `504` được retry theo exponential backoff; header `Retry-After` được tôn trọng trong giới hạn cấu hình.
- Redirect được kiểm tra lại theo allowlist/SSRF policy. Redirect hoặc anchor asset hạ từ HTTPS xuống HTTP không bao giờ được tải bằng HTTP; crawler chỉ thử nâng đích đến thành HTTPS, sau đó kiểm tra lại host/DNS, kích thước và chữ ký PDF/DOCX/DOC.
- Với nguồn `generic_web`, các alias tài nguyên công khai thông dụng như `cms`, `static`, `cdn`, `media`, `files`, `download`, `uploads` cùng domain gốc được cho phép có giới hạn để xử lý tệp đính kèm; adapter chuyên biệt vẫn giữ allowlist chính xác.
- URL được canonicalize, bỏ tracking parameter, fragment và query trùng; `?page=1` được gộp với URL danh sách gốc. Frontier ưu tiên tài liệu/đính kèm trước trang danh sách và lưu bền trong SQLite.
- Link phân trang không tiêu tốn content depth hoặc document `limit`. Một seed URL có thể lần theo trang 2, 3... đến khi chạm `max_pagination_pages`, hard limit HTTP, không còn nút trang kế tiếp hoặc URL rời khỏi phạm vi HTTPS/host được phép.
- Trang `m.mattran.org.vn/van-ban-huong-dan.html` có parser record riêng: mỗi cặp dòng `Loại văn bản/Ngày ban hành` + `Trích yếu` tạo một observation ổn định; trang danh sách chỉ dùng để discovery. Attachment liên quan giữ `parent_canonical_keys` trong metadata khi có thể.
- Dashboard tách riêng `văn bản chính / limit`, số trang danh sách, tổng record phát hiện, attachment đọc được và tổng observation. `crawler.max_total_resources` là hard limit HTTP độc lập (mặc định 2.000) để tránh vòng crawl không giới hạn.
- `header`, `footer`, `nav`, quảng cáo, menu và link điều hướng không liên quan bị loại trước khi trích xuất/chunking để giảm chunk trùng và URL rác.
- Mỗi tài liệu hoàn tất có checkpoint atomic. Chạy lại cùng `job_id` sau khi tiến trình bị ngắt sẽ nạp checkpoint đã xác minh SHA-256 và chỉ cào phần frontier còn lại.
- `ETag`/`Last-Modified` được dùng cho conditional GET. Response `304` chỉ tái sử dụng raw artifact khi đúng adapter và checksum còn hợp lệ.
- Trước conditional GET, raw artifact phải còn nằm trong `storage/raw`, tồn tại
  và đúng SHA-256; nếu không, validators/cache pointer được xóa và crawler tải
  mới ngay. Khi xóa job, mọi cache pointer trỏ vào `raw/<job_id>` cũng bị vô
  hiệu hóa, nên có thể tái sử dụng cùng Job ID mà không resume/cache nhầm dữ liệu
  đã xóa.
- PDF có text layer được đọc trực tiếp. PDF scan dùng Tesseract OCR với model
  `vie+eng`; ảnh trang được giảm kích thước theo ngân sách pixel và mỗi trang có
  timeout riêng. OCR chỉ xử lý tối đa `ocr.max_pages` mỗi tài liệu; tài liệu dài
  hơn vẫn được xuất nhưng có quality `truncated` và metadata ghi rõ số trang đã
  xử lý/bỏ qua.
- Response vẫn có hard limit để chống tiêu thụ bộ nhớ ngoài kiểm soát. Mặc định
  là 32 MiB, đủ cho tài liệu 26,36 MiB đã kiểm tra; chỉ tăng thêm cho nguồn tin
  cậy sau khi xem số trang và chi phí OCR.

Các tham số tương ứng nằm trong `config/settings.yaml`: `retry_attempts`,
`retry_backoff_base_seconds`, `retry_max_backoff_seconds`,
`per_host_delay_seconds`, `per_host_max_concurrent` và
`max_pagination_pages`. Giới hạn OCR nằm trong nhóm `ocr`: `max_pages`,
`max_image_pixels`, `page_timeout_seconds` và `tessdata_dir`.
Giới hạn chuyển đổi `.doc` nằm trong nhóm `legacy_doc`: `soffice_cmd`,
`timeout_seconds` và `max_output_bytes`.

### 5.3. Chính sách chunk thích ứng

Chunker dùng ba ngưỡng thay vì coi mọi chunk vượt target là bất thường:

- `target_tokens: 448`: kích thước đóng gói ưu tiên;
- `soft_max_tokens: 480`: cho phép giữ nguyên một đơn vị ngữ nghĩa không nên cắt;
- `max_tokens: 512`: giới hạn cứng, chunk vượt mức này không hợp lệ;
- `overlap_tokens: 64`: ngân sách overlap tối đa; overlap được bỏ nếu làm chunk
  vượt soft ceiling.

Ranh giới được ưu tiên theo thứ tự heading/đoạn, câu hoặc dòng danh sách, từ và
cuối cùng là ký tự đối với token đơn lẻ quá dài. Package mới ghi cả soft/hard
limit trong `chunk-sets.jsonl`. Package cũ không có hai trường này vẫn được đọc;
Inspector giữ cách cảnh báo theo target của package cũ.

Token hiện được ước lượng bằng tokenizer khai báo trong cấu hình
(`heuristic:vietnamese-word-1.3x`). Trước khi thay embedding model production,
cần đo lại bằng tokenizer thật thay vì chỉ nới hard limit.

### 5.4. Kiểm tra package bằng RAG Inspector

`preview.html` là một file độc lập, không tải thư viện hoặc tài nguyên ngoài.
Dashboard mở file trong iframe sandbox; có thể bấm **Mở tab mới** khi cần nhiều
không gian hơn.

Các khu vực chính:

- **Tổng quan**: extraction quality, phân bố token, nguồn, MIME và vấn đề ưu tiên;
- **Văn bản**: lọc theo chất lượng, nguồn, MIME và mở observation trong drawer;
- **Chunks**: tìm toàn văn, lọc theo văn bản/phân loại/issue và xem text đầy đủ;
- **Vấn đề**: lỗi, cảnh báo và thông tin từ các rule RAG Health; lỗi crawler hoặc chunk trùng cùng nguyên nhân được nhóm thành một dòng có số lần xuất hiện và URL mẫu;
- **Kỹ thuật**: chunk sets, tokenizer, declared/actual count và manifest JSON.

Các bảng chỉ render 50 dòng mỗi trang để tránh DOM quá lớn. Nhấn `/` để focus
ô tìm kiếm của tab hiện tại; nhấn `Esc` để đóng drawer. Tab đang mở được giữ
trong URL hash.

RAG Health là kiểm tra trước khi nạp, không phải chứng nhận dữ liệu đúng tuyệt
đối. Package mới chỉ cảnh báo token khi vượt soft ceiling và báo lỗi khi vượt
hard limit. Cần review lỗi/cảnh báo về manifest count, quan hệ mồ côi, token budget,
offset, ACL, extraction, duplicate và crawler error trước khi chạy ingestion CLI.

---

## 6. Kiểm tra và nạp có kiểm soát vào PostgreSQL & Qdrant

Theo `Project-Document/02-architecture/03-ai-rag-design.md`, legal package đi qua
`validate → admit → publish`. `publish` cưỡng chế source registry, provenance,
metadata hiệu lực và receipt gắn với digest package; observation không đạt được
quarantine. T4-03 vẫn `[~]` cho đến khi baseline 45 ca regression cộng legal
fixture đạt gate, nên chỉ publish corpus đã được người có thẩm quyền phê duyệt
trong môi trường được phép; việc command chạy thành công không phải bằng chứng
production-ready.

Từ thư mục gốc DigitalOps, áp EF migration một lần:

```powershell
dotnet ef database update `
  --project DigitalOps.API/DigitalOps.API.csproj `
  --startup-project DigitalOps.API/DigitalOps.API.csproj
```

Cấu hình connection string và các dịch vụ loopback ngoài source control:

```powershell
$env:ConnectionStrings__DigitalOps = "<PostgreSQL connection string>"
$env:Ai__Ollama__BaseUrl = "http://127.0.0.1:11434"
$env:Ai__Qdrant__ApiKey = "<Qdrant API key>"
$env:Rag__QdrantGrpcHost = "127.0.0.1"
$env:Rag__QdrantGrpcPort = "6334"
```

Kiểm tra package hoặc dry-run không gọi mạng và không ghi dữ liệu:

```powershell
dotnet run --project tools/DigitalOps.RagIngestion -- validate --staging-dir tools/rag-data-scraper/storage/staging/<job_id>
dotnet run --project tools/DigitalOps.RagIngestion -- plan --staging-dir tools/rag-data-scraper/storage/staging/<job_id>
```

Tạo receipt sau khi người quản trị dữ liệu xem kết quả validation/RAG Inspector:

```powershell
$registry = "tools/rag-data-scraper/config/source-registry.json"
$staging = "tools/rag-data-scraper/storage/staging/<job_id>"
dotnet run --project tools/DigitalOps.RagIngestion -- admit --staging-dir $staging --source-registry $registry --approved-by "<data steward>" --approval-reference "<approval id>"
```

Lệnh ghi `admission.json`, liệt kê observation được duyệt/quarantine và trả exit
code `5` nếu không có observation nào đủ điều kiện. Không sửa core staging file
sau bước này; nếu sửa phải validate/admit lại.

Sau admission, nạp có kiểm soát; thêm `--resume` khi tiếp tục job bị gián đoạn:

```powershell
dotnet run --project tools/DigitalOps.RagIngestion -- publish --staging-dir $staging --source-registry $registry
dotnet run --project tools/DigitalOps.RagIngestion -- publish --staging-dir $staging --source-registry $registry --resume
```

`DigitalOps.RagIngestion` là CLI độc lập chạy một lần rồi thoát. Script hoặc
orchestrator ngoài gọi `validate`, `plan`, `admit`, `publish` và dùng exit code; tool
không mở thêm HTTP endpoint. Flag cũ vẫn là compatibility alias tạm thời.
