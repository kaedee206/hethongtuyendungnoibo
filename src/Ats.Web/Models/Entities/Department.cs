using System.ComponentModel.DataAnnotations.Schema;
using Ats.Web.Models.Enums;

namespace Ats.Web.Models.Entities;

public class Department : BaseEntity
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public Guid? ManagerId { get; set; }
    public bool IsActive { get; set; } = true;
    public int Level { get; set; } = 1;
    public string Path { get; set; } = string.Empty;
    
    public Department? Parent { get; set; }
    public ICollection<Department> Children { get; set; } = new List<Department>();
    public User? Manager { get; set; }
    public ICollection<JobPosition> JobPositions { get; set; } = new List<JobPosition>();
}
