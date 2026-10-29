namespace Ats.Web.Models.ViewModels.EmailTemplates;

public class EmailTemplateManagementViewModel
{
    public List<EmailTemplateItemViewModel> Templates { get; set; } = [];
    public List<CandidateTemplateOption> Candidates { get; set; } = [];
    public List<JobTemplateOption> Jobs { get; set; } = [];
    public List<InterviewTemplateOption> Interviews { get; set; } = [];
}

public class EmailTemplateItemViewModel
{
    public string TemplateType { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public bool IsHtml { get; set; } = true;
    public List<string> SupportedVariables { get; set; } = [];
    public List<string> RequiredVariables { get; set; } = [];
}

public class EmailTemplateUpdateInput
{
    public string TemplateType { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public bool IsHtml { get; set; } = true;
}

public class EmailTemplatePreviewRequest
{
    public string TemplateType { get; set; } = string.Empty;
    public Guid? CandidateId { get; set; }
    public Guid? JobId { get; set; }
    public Guid? InterviewId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}

public class EmailTemplatePreviewResponse
{
    public bool Success { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Html { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}

public class CandidateTemplateOption
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}

public class JobTemplateOption
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}

public class InterviewTemplateOption
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}
