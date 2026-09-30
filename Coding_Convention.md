_HỆ THỐNG TUYỂN DỤNG NỘI BỘ (ATS) — CODING CONVENTIONS_ 

# ● **QUY TẮC ĐẶT TÊN VÀ CODING CONVENTION** 

● _Dự án: Hệ thống Tuyển dụng Nội bộ (ATS)_ 

_Stack: ASP.NET Core 9 MVC & PostgreSQL_ 

## ● **1. Quy tắc tổng quan (General Casing Standards)** 

- Tài liệu này quy định chuẩn hóa việc đặt tên (Naming Conventions) và viết mã nguồn cho toàn bộ thành viên dự án phát triển Hệ thống Tuyển dụng Nội bộ (ATS). Nhóm phát triển bắt buộc tuân thủ các quy chuẩn sau: 

|**Quy chuẩn**|**Quy tắc viết**|**Ví dụ áp dụng**|
|---|---|---|
|**PascalCase**|Viết hoa chữ cái đầu mỗi từ|Class, Interface, Method, Public<br>Property, Namespace, File .cs,<br>Razor View|
|**camelCase**|Viết thường từ đầu, viết hoa<br>chữ cái đầu các từ sau|Local variable, Parameter truyền<br>vào Method|
|**_camelCase**|Dấu gạch dưới _ +<br>camelCase|Private field trong<br>Class/Controller (Dependency<br>Injection)|
|**snake_case**|Viết thường toàn bộ, cách<br>nhau bằng dấu _|PostgreSQL Table, Column,<br>Index, Constraint|
|**kebab-case**|Viết thường toàn bộ, cách<br>nhau bằng dấu -|URL API/MVC Route, File static<br>assets (site-custom.css,<br>pipeline.js)|



● 

## ● **2. Quy tắc đặt tên trong kiến trúc ASP.NET Core 9 MVC** 

### ● **2.1. Controllers (Controllers/)** 

Trang 1 

_HỆ THỐNG TUYỂN DỤNG NỘI BỘ (ATS) — CODING CONVENTIONS_ 

- **• Tên Class Controller:** Bắt buộc kết thúc bằng hậu tố **Controller** và đặt tên theo danh từ số nhiều (ví dụ: _CandidatesController, RequisitionsController, InterviewsController_ ). 

- **• Action Method:** Đặt tên theo động từ/hành động dạng **PascalCase** , khớp với tên File View tương ứng (ví dụ: _Index, Create, Edit, Details, Delete, Approve_ ). 

- **• Hàm Async Action:** Các Action trả về View không bắt buộc có hậu tố Async để giữ giao diện Route đơn giản, nhưng các hàm xử lý bất đồng bộ ở tầng Service bên dưới bắt buộc phải có hậu tố Async. 

### ● **2.2. Models & ViewModels (Models/)** 

- Phân biệt rõ ràng giữa Entity Model (ánh xạ CSDL) và ViewModel / InputModel (giao diện UI): 

- **• Entity Class:** Danh từ số ít, trùng tên với thực thể nghiệp vụ (ví dụ: _Candidate, Requisition, InterviewSchedule_ ). 

- **• ViewModel (Hiển thị dữ liệu lên Razor View):** Đặt tên dạng **{Feature/Entity}{Action/Usage}ViewModel** (ví dụ: _CandidateListViewModel, RequisitionDetailViewModel_ ). 

- **• InputModel (Xử lý Submit Form):** Đặt tên dạng **{Feature}{Action}InputModel** hoặc **{Feature}{Action}ViewModel** (ví dụ: _CandidateCreateViewModel, OfferApproveInputModel_ ). 

- **2.3. Razor Views, Partial Views & ViewComponents (Views/)** 

- **• Standard View (.cshtml):** Trùng tên 100% với Action Method trong Controller (ví dụ: _Index.cshtml, Create.cshtml, Details.cshtml_ ). 

- **• Partial View:** Bắt đầu bằng dấu gạch dưới **_** + PascalCase (ví dụ: __CandidateCardPartial.cshtml, _PipelineStage.cshtml_ ). 

- **• Layout & Shared:** Đặt trong thư mục **Views/Shared/** và có tiền tố **_** (ví dụ: __Layout.cshtml, _Header.cshtml, _ValidationScriptsPartial.cshtml_ ). 

- **• ViewComponent:** Class nằm trong **ViewComponents/** kết thúc bằng hậu tố **ViewComponent** . View tương ứng đặt tại _Views/Shared/Components/{ViewComponentName}/Default.cshtml_ . 

- Mẫu Controller chuẩn ASP.NET Core 9 MVC: 

Trang 2 

_HỆ THỐNG TUYỂN DỤNG NỘI BỘ (ATS) — CODING CONVENTIONS_ 

- public class CandidatesController(ICandidateService candidateService) : Controller 

- { 

- private readonly ICandidateService _candidateService = candidateService; 

- ● // GET: /Candidates ● [HttpGet] 

- public async Task<IActionResult> Index(CancellationToken cancellationToken) 

- ● { 

- var candidates = await _candidateService.GetAllAsync(cancellationToken); 

- return View(candidates); // Trả về Views/Candidates/Index.cshtml 

- ● } 

- 

- // POST: /Candidates/Create 

- ● [HttpPost] 

- [ValidateAntiForgeryToken] 

- public async Task<IActionResult> Create(CandidateCreateViewModel model, 

- CancellationToken cancellationToken) 

- ● { 

- if (!ModelState.IsValid) 

- return View(model); 

- 

- await _candidateService.CreateAsync(model, cancellationToken); 

- return RedirectToAction(nameof(Index)); 

- ● } ● } 

Trang 3 

_HỆ THỐNG TUYỂN DỤNG NỘI BỘ (ATS) — CODING CONVENTIONS_ 

## ● **3. Quy tắc đặt tên C# & .NET 9 General** 

- **• Interface:** Bắt buộc có tiền tố **I** + PascalCase (ví dụ: _ICandidateRepository, IRequisitionService_ ). 

- **• Private Readonly Fields:** Sử dụng **_camelCase** cho các dependency được inject qua Constructor (ví dụ: _private readonly ICandidateService _candidateService;_ ). 

- **• Method & Async:** Viết PascalCase, bắt đầu bằng động từ. Mọi method xử lý async phải có hậu tố **Async** và nhận **CancellationToken** . 

- **• Enum & Exception:** Enum dạng số ít (ví dụ: _CandidateStatus_ ). Exception tùy chỉnh phải có hậu tố **Exception** (ví dụ: _RequisitionNotFoundException_ ). 

## ● **4. Quy tắc đặt tên Cơ sở dữ liệu PostgreSQL** 

- PostgreSQL phân biệt hoa thường và mặc định chuyển đổi tên thành chữ thường. Để tránh việc phải sử dụng dấu ngoặc kép trong các câu lệnh SQL, toàn bộ Database Object bắt buộc tuân theo quy chuẩn snake_case. 

- **• Tên Bảng (Tables):** Dùng **snake_case** danh từ **số nhiều** (ví dụ: _candidates, requisitions, interview_schedules, user_roles_ ). 

- **• Tên Cột (Columns):** Dùng **snake_case** danh từ **số ít** (ví dụ: _id, full_name, email, applied_date, created_at_ ). 

- **• Khoá chính & Khoá ngoại:** Khoá chính đặt là **id** (UUID). Khoá ngoại đặt theo công thức **{target_table_singular}_id** (ví dụ: _requisition_id, candidate_id, created_by_user_id_ ). 

- **• Ràng buộc & Index:** Primary Key: **pk_{table_name}** | Foreign Key: **fk_{source_table} _{target_table}** | Unique: **uq_{table_name}_{column_name}** | Index: **idx_{table_name} _{column_name}** . 

- Cấu hình tự động Naming Convention trong Entity Framework Core 9 (Program.cs): 

- builder.Services.AddDbContext<ApplicationDbContext>(options => 

- options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")) 

- .UseSnakeCaseNamingConvention()); // Tự động ánh xạ PascalCase C# -> snake_case PostgreSQL 

Trang 4 

_HỆ THỐNG TUYỂN DỤNG NỘI BỘ (ATS) — CODING CONVENTIONS_ 

## ● **5. Quy tắc tổ chức Thư mục & File (Folder Structure)** 

- Dự án ATS tuân thủ cấu trúc ASP.NET Core 9 MVC phân tầng chuẩn: 

- src/Ats.Web/ 

- ├── Controllers/ # Các Controller xử lý request 

- ● │├── CandidatesController.cs ● │├── RequisitionsController.cs # Quản lý yêu cầu tuyển dụng 

- │├── InterviewsController.cs 

- │└── HomeController.cs 

- │ 

- ├── Models/ # Domain Entities & ViewModels 

- ● │├── Entities/ # Model ánh xạ CSDL PostgreSQL ● ││├── Candidate.cs ● ││└── Requisition.cs ● │└── ViewModels/ # ViewModels truyền dữ liệu Razor View 

- │ ├── Candidates/ 

- ● │ │├── CandidateListViewModel.cs 

- │ │└── CandidateCreateViewModel.cs 

- │ └── Requisitions/ 

- ● │ └── RequisitionApproveViewModel.cs 

- │ 

- ├── Views/ # Razor Views (.cshtml) 

- │├── Candidates/ 

- ││├── Index.cshtml 

- ● ││├── Details.cshtml 

- ││└── _CandidatePipelinePartial.cshtml # Partial View 

Trang 5 

_HỆ THỐNG TUYỂN DỤNG NỘI BỘ (ATS) — CODING CONVENTIONS_ 

- │├── Requisitions/ 

- ● ││├── Index.cshtml ● ││└── Approve.cshtml ● │├── Shared/ # Shared Layouts & Components ● ││├── _Layout.cshtml ● ││└── _Header.cshtml ● │├── _ViewImports.cshtml ● │└── _ViewStart.cshtml ● │ ● ├── Services/ # Business Logic Layer ● │├── Interfaces/ ● ││└── ICandidateService.cs ● │└── CandidateService.cs ● │ ● ├── Data/ # EF Core DbContext & Migrations ● │├── ApplicationDbContext.cs ● │└── Migrations/ ● │ ● └── wwwroot/ # Static Files (CSS/JS Client-side) ● ├── css/ ● │└── site.css ● └── js/ ● ├── candidate-pipeline.js ● └── requisition-approval.js 

## ● **6. Quy tắc đặt tên Feature, API Route & DTOs** 

Trang 6 

_HỆ THỐNG TUYỂN DỤNG NỘI BỘ (ATS) — CODING CONVENTIONS_ 

### ● **6.1. Mã hóa Feature / Epic trong dự án ATS** 

- Dự án được phân chia theo các mã Epic tiêu chuẩn: 

- **• EP-01_UserRole:** Tài khoản, Phân quyền & Hồ sơ cá nhân 

- **• EP-02_Organization:** Danh mục Cơ cấu Tổ chức & Vị trí công việc 

- **• EP-03_Requisition:** Yêu cầu tuyển dụng & Luồng Phê duyệt 

- **• EP-04_JobPosting:** Đăng tin tuyển dụng & Cổng tuyển dụng nội bộ 

- **• EP-05_CandidatePipeline:** Quản lý Hồ sơ ứng viên & Pipeline tuyển dụng 

- **• EP-06_InterviewEvaluation:** Lịch phỏng vấn & Đánh giá ứng viên 

- **• EP-07_OfferOnboarding:** Tạo Offer letter & Quy trình Onboarding 

- **• EP-08_NotificationEmail:** Hệ thống Thông báo & Email tự động 

- **• EP-09_AnalyticsDashboard:** Báo cáo Tuyển dụng & Analytics Dashboard 

- **6.2. Route URLs trong ASP.NET Core MVC** 

- Sử dụng kebab-case cho các đường dẫn URL custom trong Attribute Routing: 

|**Method**|**Route URL**|**Mô tả chức năng**|
|---|---|---|
|**GET**|/yeu-cau-tuyen-dung|Danh sách yêu cầu tuyển<br>dụng|
|**POST**|/yeu-cau-tuyen-dung/tao-moi|Xử lý tạo yêu cầu tuyển dụng|
|**GET**|/yeu-cau-tuyen-dung/{id}/phe-<br>duyet|Giao diện phê duyệt yêu cầu|
|**POST**|/ung-vien/{id}/chuyen-trang-thai|Cập nhật pipeline ứng viên|



Trang 7 

_HỆ THỐNG TUYỂN DỤNG NỘI BỘ (ATS) — CODING CONVENTIONS_ 

- 

- **7. C# 12 & .NET 9 MVC Best Practices** 

- **• Primary Constructors:** Sử dụng Primary Constructors cho Controller và Service để inject dependency ngắn gọn, loại bỏ boilerplate code. 

- **• Tag Helpers:** Sử dụng triệt để Tag Helpers trong Razor View (asp-controller, asp-action, asp-for, asp-validation-for) thay vì gõ tay thuộc tính HTML. 

- **• Bảo mật Form (Anti-Forgery):** Mọi HTTP POST Action xử lý submit Form bắt buộc phải có thuộc tính [HttpPost] và [ValidateAntiForgeryToken]. 

- **• Clean Controller:** Controller chỉ đóng vai trò điều phối (nhận request, gọi Service, chuẩn bị ViewModel và trả về View). Không viết logic nghiệp vụ trực tiếp trong Controller. 

- **• Đồng bộ Múi giờ (UTC & TIMESTAMPTZ):** Toàn bộ dữ liệu thời gian phải lưu dưới dạng UTC (TIMESTAMPTZ trong PostgreSQL). Khi hiển thị ra Razor View, định dạng theo múi giờ Vietnam (Asia/Ho_Chi_Minh) và Culture vi-VN. 

Trang 8 

