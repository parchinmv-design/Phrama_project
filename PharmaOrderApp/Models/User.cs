namespace PharmaOrderApp.Models;

public enum UserRole
{
    Guest = 0,
    Client = 1,
    Manager = 2,
    Admin = 3
}

public sealed class User
{
    public int Id { get; set; }
    public string Login { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public int AssignedPharmacyId { get; set; }
    public string AssignedPharmacyName { get; set; } = string.Empty;
    public string StatusName { get; set; } = "Active";
    public bool IsActive => string.Equals(StatusName, "Active", StringComparison.OrdinalIgnoreCase);

    public string RoleTitle => Role switch
    {
        UserRole.Admin => "Администратор",
        UserRole.Manager => "Менеджер",
        UserRole.Client => "Клиент",
        _ => "Гость"
    };
}
