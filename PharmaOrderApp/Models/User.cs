namespace PharmaOrderApp.Models;

public enum UserRole
{
    Guest = 0,
    Client = 1,
    Pharmacist = 2,
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

    public string RoleTitle => Role switch
    {
        UserRole.Admin => "Администратор",
        UserRole.Pharmacist => "Фармацевт",
        UserRole.Client => "Клиент",
        _ => "Гость"
    };
}
