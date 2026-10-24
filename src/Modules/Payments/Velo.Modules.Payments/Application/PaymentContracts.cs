namespace Velo.Modules.Payments.Application;
public sealed record SettlementRequest(Guid OperationId, Guid DriverId, Guid[] OrderIds, decimal AmountReceived, string Reference);
public sealed record SettlementResponse(Guid Id, Guid DriverId, decimal Amount, string Reference, Guid[] OrderIds, DateTimeOffset CreatedAt);
public sealed record CashBalanceResponse(Guid DriverId, decimal OutstandingCash, string Currency, int UnsettledOrders);
public sealed record PaymentPage(Contracts.PaymentReceipt[] Items, int Page, int PageSize, int TotalCount);
public static class PaymentPolicies
{
    public const string Manage = "Payments.Manage";
    public const string Permission = "payments.manage";
}
