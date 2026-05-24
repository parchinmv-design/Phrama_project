namespace PharmaOrderApp.Models;

public sealed class EmployeeSummary
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Login { get; set; } = string.Empty;
    public string RoleTitle { get; set; } = string.Empty;
    public string Pharmacy { get; set; } = string.Empty;
    public string StatusTitle { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
}
