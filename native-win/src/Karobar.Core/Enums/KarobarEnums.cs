namespace Karobar.Core.Enums;

public enum InvoiceType
{
    Debit,
    Credit
}

public enum SaleType
{
    Retail,
    Wholesale
}

public enum WholesaleType
{
    Unit,
    Pack
}

public enum PaymentMethod
{
    Cash,
    Bank,
    Cheque,
    Card,
    Online,
    Credit
}

public enum InvoiceStatus
{
    Draft,
    Posted,
    Reversed,
    Cancelled
}

public enum SettlementMode
{
    CashRefund,
    LedgerCredit,
    CreditNote,
    InvoiceAdjustment
}

public enum PartyType
{
    Customer,
    Supplier
}

public enum LedgerEntryType
{
    Invoice,
    Payment,
    Manual
}

public enum CashDrawerStatus
{
    Open,
    Closed
}

public enum AccountType
{
    Cash,
    Bank,
    Wallet,
    Asset,
    Liability,
    Equity,
    Revenue,
    Expense
}

public enum NormalBalance
{
    Debit,
    Credit
}

public enum StockDisposalStatus
{
    Quarantined,
    Disposed,
    Restored
}

public enum StockDisposalCause
{
    ShelfExpiry,
    BreakageDamage,
    TemperatureSpoilage,
    CustomerReturnDefect,
    ManufacturerRecall,
    WaterPestDamage,
    TheftShrinkage,
    Other
}

public enum StockDisposalMethod
{
    Incineration,
    ChemicalNeutralization,
    MunicipalWaste,
    SupplierDestruction,
    DrainFlush,
    QuarantineHolding,
    Other
}

public enum StocktakeSessionType
{
    Full,
    Cycle
}

public enum StocktakeStatus
{
    Draft,
    InProgress,
    PendingReview,
    Finalized,
    Cancelled
}

public enum StocktakeItemStatus
{
    Pending,
    Counted
}

public enum TradeSchemeType
{
    Ratio,
    Slab
}

public enum TradeSchemeTargetType
{
    Product,
    Brand,
    Category
}

public enum TradeSchemeBonusType
{
    Pack,
    Unit
}

public enum BackupType
{
    Full,
    Database,
    Settings
}

public enum BackupStatus
{
    Pending,
    Completed,
    Failed,
    Restored
}

public enum FbrSyncStatus
{
    NotApplicable,
    Pending,
    Synced,
    Failed
}

public enum UserDemandStatus
{
    Pending,
    Ordered,
    Fulfilled,
    Cancelled
}

public enum EmployeeStatus
{
    Active,
    Inactive,
    Terminated,
    OnLeave
}

public enum AttendanceStatus
{
    Present,
    Absent,
    Late,
    HalfDay,
    Leave,
    Holiday
}

public enum PayrollRunStatus
{
    Draft,
    Approved,
    Paid,
    Cancelled
}

public enum PayrollPaymentStatus
{
    Unpaid,
    Partial,
    Paid
}

public enum ChequeStatus
{
    Pending,
    Cleared,
    Bounced,
    Cancelled
}

public enum ExpiryReturnPolicy
{
    BeforeExpiry,
    AcceptsExpired,
    NonReturnable
}
