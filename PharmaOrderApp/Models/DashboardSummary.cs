namespace PharmaOrderApp.Models;

public sealed class DashboardSummary
{
    public int TotalClients { get; set; }
    public int ActiveManagers { get; set; }
    public int ActiveAdmins { get; set; }
    public int OpenOrders { get; set; }
    public int OpenSupplyRequests { get; set; }
    public decimal Revenue { get; set; }
}
