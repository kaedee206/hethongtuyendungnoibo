namespace Ats.Web.Models.Entities;

public class CompanyProfile : BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CompanyName { get; set; } = "Tập đoàn Công nghệ NoveraTech";
    public string Headline { get; set; } = "Cùng NoveraTech kiến tạo tương lai công nghệ số";
    public string AboutText { get; set; } = "NoveraTech là tập đoàn công nghệ tiên phong tại Việt Nam, phát triển các nền tảng kỹ thuật số và kiến trúc vi dịch vụ chịu tải cao.";
    public string EngineeringCulture { get; set; } = "Engineering-First, Zero Politics";
    public string TechStackJson { get; set; } = "[\".NET 9\", \"React 19\", \"PostgreSQL\", \"Clean Architecture\", \"Docker/K8s\", \"AI/LLMs\"]";
    public string ProofMetricsJson { get; set; } = "[{\"label\": \"Kỹ sư\", \"value\": \"500+\"}, {\"label\": \"Tháng lương/năm + ESOP\", \"value\": \"14–16\"}, {\"label\": \"Môi trường IT\", \"value\": \"Top 10\"}, {\"label\": \"Cam kết phản hồi\", \"value\": \"48h\"}]";
    public string PerksJson { get; set; } = "[\"Thu nhập Top 10% thị trường\", \"MacBook Pro M3/M4 + 2 màn 4K\", \"Bảo hiểm Diamond NoveraCare\", \"Ngân sách học tập $1.000/năm\", \"Hybrid 2 ngày WFH không OT\", \"Pantry 5 sao\"]";
    public string HeadquartersAddress { get; set; } = "Tầng 18 Novera Tower, Cầu Giấy, Hà Nội";
    public string ContactEmail { get; set; } = "careers@noveratech.digital";
    public string PhoneContact { get; set; } = "(+84) 24 3999 8888";
}
