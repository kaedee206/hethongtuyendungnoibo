namespace Ats.Web.Models.ViewModels.Account;

public class GoogleConfigGuideViewModel
{
    public string? ReturnUrl { get; set; }
    public string UserType { get; set; } = "candidate"; // candidate or staff
    public bool IsConfigured { get; set; }
    public string CallbackUrl { get; set; } = "http://localhost:5266/signin-google";
}
