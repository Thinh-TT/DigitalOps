# AI RAG Evaluation — T0-00

Thư mục này lưu fixture có version dùng làm bằng chứng phê duyệt kiến trúc AI
local-first của DigitalOps. Runner nằm ngoài production solution tại
`tools/ai-rag-eval/Invoke-T000Evaluation.ps1`.

## Phạm vi fixture

`t0-00-cases.json` có đúng 45 ca:

| Nhóm | Số ca | Nội dung chính |
| --- | ---: | --- |
| Retrieval | 12 | Staff, template, FormatRules, source inactive/restricted |
| Assignment | 12 | 8 ca đủ dữ liệu và 4 ca phải abstain |
| Draft | 9 | Bảy loại template, chống bịa dữ kiện và prompt injection |
| Review | 12 | Rule xác định, cảnh báo AI và nội dung adversarial |

Fixture chỉ chứa dữ liệu tổng hợp, không chứa dữ liệu hội viên, email, số điện
thoại, attachment hoặc nội dung văn bản thật.

## Cách chạy

Yêu cầu Ollama và Qdrant chỉ lắng nghe trên loopback. Truyền Qdrant API key qua
biến môi trường `QDRANT_API_KEY`, không ghi secret vào repository. Trước khi
chạy, máy phải có ít nhất 8 GB RAM khả dụng; runner dừng trước khi tạo collection
nếu preflight không đạt. Khi script điều phối đo RAM ngay trước lúc khởi động hai
service, nó truyền lại số đo đó qua `-PreflightAvailableMemoryGb`; runner đồng
thời ghi riêng RAM khả dụng tại thời điểm workload bắt đầu.

~~~powershell
$env:QDRANT_API_KEY = '<local-secret>'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\ai-rag-eval\Invoke-T000Evaluation.ps1
~~~

Runner tạo lại collection chỉ dành cho evaluation `digitalops_t000_eval`, gọi
Ollama bằng structured JSON output, đo cold/warm latency, peak RAM của các process
`ollama`/`llama-server` và container `digitalops-t000-qdrant`, các gate tự động
rồi ghi kết quả chi tiết vào
`%TEMP%\digitalops-t0-00-evaluation-results.json`.
Session log T0-00 chỉ ghi metric tổng hợp, model digest và quyết định phê duyệt;
không ghi raw prompt/completion nhạy cảm.

## Gate chấm bản nháp

Gate tự động kiểm tra JSON schema, heading bắt buộc và các cụm từ bị cấm. Sau
đó Project Owner chấm chín bản nháp theo thang 1–5 cho tiếng Việt, bám template
và không bịa dữ kiện. Tối thiểu 8/9 bản phải đạt từ 4 điểm. Không chuyển AI RAG
Design sang Approved hoặc đánh dấu T0-00 hoàn thành khi gate này chưa đạt.
