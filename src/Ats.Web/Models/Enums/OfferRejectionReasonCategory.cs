using System.ComponentModel.DataAnnotations;

namespace Ats.Web.Models.Enums;

/// <summary>
/// Danh mục lý do ứng viên từ chối Offer (User Story SP-7).
/// </summary>
public enum OfferRejectionReasonCategory
{
    [Display(Name = "Lương / Đãi ngộ (Compensation)")]
    Salary = 1,

    [Display(Name = "Địa điểm / Khoảng cách (Location)")]
    Location = 2,

    [Display(Name = "Đã nhận việc nơi khác (Accepted Another Offer)")]
    CompetitorOffer = 3,

    [Display(Name = "Lý do cá nhân (Personal / Family)")]
    Personal = 4,

    [Display(Name = "Khác (Other)")]
    Other = 5
}

public static class OfferRejectionReasonCategoryExtensions
{
    public static string GetDisplayName(this OfferRejectionReasonCategory category) => category switch
    {
        OfferRejectionReasonCategory.Salary => "Lương / Đãi ngộ (Compensation)",
        OfferRejectionReasonCategory.Location => "Địa điểm / Khoảng cách (Location)",
        OfferRejectionReasonCategory.CompetitorOffer => "Đã nhận việc nơi khác (Accepted Another Offer)",
        OfferRejectionReasonCategory.Personal => "Lý do cá nhân (Personal / Family)",
        OfferRejectionReasonCategory.Other => "Khác (Other)",
        _ => category.ToString()
    };
}
