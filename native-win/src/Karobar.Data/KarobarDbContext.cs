using System.Globalization;
using Karobar.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Karobar.Data;

public class KarobarDbContext : DbContext
{
    private static readonly HashSet<string> PureDateColumnNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "date",
        "due_date",
        "entry_date",
        "posted_date",
        "payment_date",
        "cheque_date",
        "disposal_date",
        "joining_date",
        "start_date",
        "end_date",
        "usage_date",
        "expiry_date",
        "expiry",
        "next_due_date",
        "next_refill_date",
        "expected_received_date"
    };

    public KarobarDbContext(DbContextOptions<KarobarDbContext> options)
        : base(options)
    {
    }

    // -------------------------------------------------------------------------
    // Auth, Users, Roles, Permissions, Licensing & Sequences
    // -------------------------------------------------------------------------
    public DbSet<User> Users => Set<User>();
    public DbSet<PersonalAccessToken> PersonalAccessTokens => Set<PersonalAccessToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<ModelHasRole> ModelHasRoles => Set<ModelHasRole>();
    public DbSet<ModelHasPermission> ModelHasPermissions => Set<ModelHasPermission>();
    public DbSet<RoleHasPermission> RoleHasPermissions => Set<RoleHasPermission>();
    public DbSet<License> Licenses => Set<License>();
    public DbSet<Terminal> Terminals => Set<Terminal>();
    public DbSet<DocumentSequence> DocumentSequences => Set<DocumentSequence>();

    // -------------------------------------------------------------------------
    // Master Catalog, Batches, Parties & Barcodes
    // -------------------------------------------------------------------------
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Formulation> Formulations => Set<Formulation>();
    public DbSet<StorageLocation> StorageLocations => Set<StorageLocation>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Batch> Batches => Set<Batch>();
    public DbSet<ProductSupplier> ProductSuppliers => Set<ProductSupplier>();
    public DbSet<SupplierProductPosition> SupplierProductPositions => Set<SupplierProductPosition>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerRegularItem> CustomerRegularItems => Set<CustomerRegularItem>();
    public DbSet<CustomerWholeSalePrice> CustomerWholeSalePrices => Set<CustomerWholeSalePrice>();
    public DbSet<Doctor> Doctors => Set<Doctor>();
    public DbSet<BarcodeTemplate> BarcodeTemplates => Set<BarcodeTemplate>();
    public DbSet<ColdChainLog> ColdChainLogs => Set<ColdChainLog>();

    // -------------------------------------------------------------------------
    // Procurement, Sales, POS, Returns, Schemes, Drafts & Demands
    // -------------------------------------------------------------------------
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();
    public DbSet<PurchaseInvoice> PurchaseInvoices => Set<PurchaseInvoice>();
    public DbSet<PurchaseInvoiceItem> PurchaseInvoiceItems => Set<PurchaseInvoiceItem>();
    public DbSet<PurchaseReturn> PurchaseReturns => Set<PurchaseReturn>();
    public DbSet<PurchaseReturnItem> PurchaseReturnItems => Set<PurchaseReturnItem>();
    public DbSet<SaleInvoice> SaleInvoices => Set<SaleInvoice>();
    public DbSet<SaleInvoiceItem> SaleInvoiceItems => Set<SaleInvoiceItem>();
    public DbSet<SaleReturn> SaleReturns => Set<SaleReturn>();
    public DbSet<SaleReturnItem> SaleReturnItems => Set<SaleReturnItem>();
    public DbSet<InvoiceDraft> InvoiceDrafts => Set<InvoiceDraft>();
    public DbSet<TradeScheme> TradeSchemes => Set<TradeScheme>();
    public DbSet<TradeSchemeSlab> TradeSchemeSlabs => Set<TradeSchemeSlab>();
    public DbSet<UserDemand> UserDemands => Set<UserDemand>();
    public DbSet<DocumentHistory> DocumentHistories => Set<DocumentHistory>();

    // -------------------------------------------------------------------------
    // Inventory Audits, Adjustments, Stocktake & Disposals
    // -------------------------------------------------------------------------
    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();
    public DbSet<StockAdjustmentItem> StockAdjustmentItems => Set<StockAdjustmentItem>();
    public DbSet<StocktakeSession> StocktakeSessions => Set<StocktakeSession>();
    public DbSet<StocktakeItem> StocktakeItems => Set<StocktakeItem>();
    public DbSet<StockDisposal> StockDisposals => Set<StockDisposal>();
    public DbSet<StockDisposalItem> StockDisposalItems => Set<StockDisposalItem>();

    // -------------------------------------------------------------------------
    // Finance, Ledgers, Treasury, Accounting GL, HR Payroll & Settings
    // -------------------------------------------------------------------------
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<ThemeSetting> ThemeSettings => Set<ThemeSetting>();
    public DbSet<BackupLog> BackupLogs => Set<BackupLog>();
    public DbSet<BackupSchedule> BackupSchedules => Set<BackupSchedule>();
    public DbSet<CubeProviderUsage> CubeProviderUsages => Set<CubeProviderUsage>();
    public DbSet<CustomerLedger> CustomerLedgers => Set<CustomerLedger>();
    public DbSet<SupplierLedger> SupplierLedgers => Set<SupplierLedger>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();
    public DbSet<CashDrawerSession> CashDrawerSessions => Set<CashDrawerSession>();
    public DbSet<CashDrawerTransaction> CashDrawerTransactions => Set<CashDrawerTransaction>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<AccountTransaction> AccountTransactions => Set<AccountTransaction>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<ChartOfAccount> ChartOfAccounts => Set<ChartOfAccount>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalEntryLine> JournalEntryLines => Set<JournalEntryLine>();
    public DbSet<TaxRate> TaxRates => Set<TaxRate>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmployeeAttendance> EmployeeAttendances => Set<EmployeeAttendance>();
    public DbSet<EmployeeAdvance> EmployeeAdvances => Set<EmployeeAdvance>();
    public DbSet<PayrollRun> PayrollRuns => Set<PayrollRun>();
    public DbSet<PayrollItem> PayrollItems => Set<PayrollItem>();

    // -------------------------------------------------------------------------
    // Infrastructure / System Tables
    // -------------------------------------------------------------------------
    public DbSet<MigrationRecord> Migrations => Set<MigrationRecord>();
    public DbSet<CacheEntry> CacheEntries => Set<CacheEntry>();
    public DbSet<CacheLock> CacheLocks => Set<CacheLock>();
    public DbSet<SessionRecord> Sessions => Set<SessionRecord>();
    public DbSet<JobRecord> Jobs => Set<JobRecord>();
    public DbSet<JobBatch> JobBatches => Set<JobBatch>();
    public DbSet<FailedJob> FailedJobs => Set<FailedJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ---------------------------------------------------------------------
        // 1. Spatie Permission Composite Primary Keys & Relationships
        // ---------------------------------------------------------------------
        modelBuilder.Entity<ModelHasPermission>(entity =>
        {
            entity.HasKey(x => new { x.PermissionId, x.ModelId, x.ModelType });
            entity.HasIndex(x => new { x.ModelId, x.ModelType })
                .HasDatabaseName("model_has_permissions_model_id_model_type_index");
            entity.HasOne(x => x.Permission)
                .WithMany(p => p.ModelPermissions)
                .HasForeignKey(x => x.PermissionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ModelHasRole>(entity =>
        {
            entity.HasKey(x => new { x.RoleId, x.ModelId, x.ModelType });
            entity.HasIndex(x => new { x.ModelId, x.ModelType })
                .HasDatabaseName("model_has_roles_model_id_model_type_index");
            entity.HasOne(x => x.Role)
                .WithMany(r => r.ModelRoles)
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RoleHasPermission>(entity =>
        {
            entity.HasKey(x => new { x.PermissionId, x.RoleId });
            entity.HasOne(x => x.Permission)
                .WithMany(p => p.RolePermissions)
                .HasForeignKey(x => x.PermissionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Role)
                .WithMany(r => r.RolePermissions)
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.HasKey(x => x.Email);
        });

        modelBuilder.Entity<CacheEntry>(entity =>
        {
            entity.HasKey(x => x.Key);
        });

        modelBuilder.Entity<CacheLock>(entity =>
        {
            entity.HasKey(x => x.Key);
        });

        modelBuilder.Entity<SessionRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
        });

        modelBuilder.Entity<JobBatch>(entity =>
        {
            entity.HasKey(x => x.Id);
        });

        // ---------------------------------------------------------------------
        // 2. Soft Delete Global Query Filters (matching Laravel SoftDeletes)
        // ---------------------------------------------------------------------
        modelBuilder.Entity<Expense>().HasQueryFilter(e => e.DeletedAt == null);
        modelBuilder.Entity<InvoiceDraft>().HasQueryFilter(d => d.DeletedAt == null);

        // ---------------------------------------------------------------------
        // 3. Explicit 1:1 & Multi-Foreign-Key Relationships
        // ---------------------------------------------------------------------
        modelBuilder.Entity<User>()
            .HasOne(u => u.Employee)
            .WithOne(e => e.User)
            .HasForeignKey<Employee>(e => e.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<PurchaseReturn>()
            .HasOne(pr => pr.SupplierLedgerEntry)
            .WithOne(sl => sl.PurchaseReturn)
            .HasForeignKey<SupplierLedger>(sl => sl.PurchaseReturnId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<SaleReturn>()
            .HasOne(sr => sr.CustomerLedgerEntry)
            .WithOne(cl => cl.SaleReturn)
            .HasForeignKey<CustomerLedger>(cl => cl.SaleReturnId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<StockAdjustment>()
            .HasOne(sa => sa.StocktakeSession)
            .WithOne(ss => ss.StockAdjustment)
            .HasForeignKey<StocktakeSession>(ss => ss.StockAdjustmentId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<StockAdjustment>()
            .HasOne(sa => sa.StockDisposal)
            .WithOne(sd => sd.StockAdjustment)
            .HasForeignKey<StockDisposal>(sd => sd.StockAdjustmentId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<SaleInvoice>(entity =>
        {
            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.ReversedBy)
                .WithMany()
                .HasForeignKey(x => x.ReversedById)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.Customer)
                .WithMany(c => c.SaleInvoices)
                .HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Doctor)
                .WithMany(d => d.SaleInvoices)
                .HasForeignKey(x => x.DoctorId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.Terminal)
                .WithMany(t => t.SaleInvoices)
                .HasForeignKey(x => x.TerminalId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<CashDrawerSession>(entity =>
        {
            entity.HasOne(x => x.Terminal)
                .WithMany(t => t.CashDrawerSessions)
                .HasForeignKey(x => x.TerminalId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PurchaseInvoice>(entity =>
        {
            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.ReversedBy)
                .WithMany()
                .HasForeignKey(x => x.ReversedById)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.PurchaseOrder)
                .WithMany(po => po.Invoices)
                .HasForeignKey(x => x.PurchaseOrderId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<JournalEntry>(entity =>
        {
            entity.HasOne(x => x.Creator)
                .WithMany()
                .HasForeignKey(x => x.CreatedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.ReversedByUser)
                .WithMany()
                .HasForeignKey(x => x.ReversedBy)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasOne(x => x.Creator)
                .WithMany()
                .HasForeignKey(x => x.CreatedById)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.ReversedBy)
                .WithMany()
                .HasForeignKey(x => x.ReversedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<StockDisposal>(entity =>
        {
            entity.HasOne(x => x.Creator)
                .WithMany()
                .HasForeignKey(x => x.CreatedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.Disposer)
                .WithMany()
                .HasForeignKey(x => x.DisposedBy)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.Restorer)
                .WithMany()
                .HasForeignKey(x => x.RestoredBy)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ---------------------------------------------------------------------
        // 4. Unique & Composite Indexes Matching database.sqlite
        // ---------------------------------------------------------------------
        modelBuilder.Entity<User>().HasIndex(x => x.Email).IsUnique();
        modelBuilder.Entity<User>().HasIndex(x => x.PinCode).IsUnique();
        modelBuilder.Entity<Employee>().HasIndex(x => x.EmployeeCode).IsUnique();
        modelBuilder.Entity<Employee>().HasIndex(x => x.PinCode).IsUnique();
        modelBuilder.Entity<PersonalAccessToken>().HasIndex(x => x.Token).IsUnique();
        modelBuilder.Entity<Role>().HasIndex(x => new { x.Name, x.GuardName }).IsUnique();
        modelBuilder.Entity<Permission>().HasIndex(x => new { x.Name, x.GuardName }).IsUnique();
        modelBuilder.Entity<Customer>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<Customer>().HasIndex(x => x.Email).IsUnique();
        modelBuilder.Entity<Supplier>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<Category>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<Brand>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<Product>().HasIndex(x => x.ProductCode).IsUnique();
        modelBuilder.Entity<Product>().HasIndex(x => x.Barcode).IsUnique();
        modelBuilder.Entity<SaleInvoice>().HasIndex(x => x.PostedNumber).IsUnique();
        modelBuilder.Entity<SaleReturn>().HasIndex(x => x.PostedNumber).IsUnique();
        modelBuilder.Entity<PurchaseReturn>().HasIndex(x => x.PostedNumber).IsUnique();
        modelBuilder.Entity<StockAdjustment>().HasIndex(x => x.PostedNumber).IsUnique();
        modelBuilder.Entity<StocktakeSession>().HasIndex(x => x.ReferenceNo).IsUnique();
        modelBuilder.Entity<StockDisposal>().HasIndex(x => x.DisposalNumber).IsUnique();
        modelBuilder.Entity<Payment>().HasIndex(x => x.PaymentNumber).IsUnique();
        modelBuilder.Entity<Expense>().HasIndex(x => x.ExpenseNumber).IsUnique();
        modelBuilder.Entity<ExpenseCategory>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<ChartOfAccount>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<JournalEntry>().HasIndex(x => x.EntryNumber).IsUnique();
        modelBuilder.Entity<TaxRate>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<StorageLocation>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<DocumentSequence>().HasIndex(x => x.SequenceKey).IsUnique();
        modelBuilder.Entity<Terminal>().HasIndex(x => x.TerminalUid).IsUnique();
        modelBuilder.Entity<InvoiceDraft>().HasIndex(x => x.Uuid).IsUnique();
        modelBuilder.Entity<Formulation>().HasIndex(x => x.NormalizedName).IsUnique();
        modelBuilder.Entity<ProductSupplier>().HasIndex(x => new { x.ProductId, x.SupplierId }).IsUnique();
        modelBuilder.Entity<SupplierProductPosition>().HasIndex(x => new { x.SupplierId, x.ProductId }).IsUnique();
        modelBuilder.Entity<CustomerWholeSalePrice>().HasIndex(x => new { x.CustomerId, x.ProductId }).IsUnique();
        modelBuilder.Entity<CustomerRegularItem>().HasIndex(x => new { x.CustomerId, x.ProductId }).IsUnique();
        modelBuilder.Entity<PurchaseOrderItem>().HasIndex(x => new { x.PurchaseOrderId, x.ProductId }).IsUnique();
        modelBuilder.Entity<EmployeeAttendance>().HasIndex(x => new { x.EmployeeId, x.Date }).IsUnique();
        modelBuilder.Entity<PayrollItem>().HasIndex(x => new { x.PayrollRunId, x.EmployeeId }).IsUnique();
        modelBuilder.Entity<CubeProviderUsage>().HasIndex(x => new { x.Provider, x.UsageDate }).IsUnique();

        // ---------------------------------------------------------------------
        // 5. Tolerant SQLite Value Converters & Default Column Values
        // ---------------------------------------------------------------------
        modelBuilder.Entity<Brand>()
            .Property(x => x.ExpiryReturnPolicy)
            .HasDefaultValue("before_expiry");

        modelBuilder.Entity<Supplier>()
            .Property(x => x.ExpiryReturnPolicy)
            .HasDefaultValue("before_expiry");

        modelBuilder.Entity<Supplier>()
            .Property(x => x.TaxStatus)
            .HasDefaultValue("registered");

        modelBuilder.Entity<Product>()
            .Property(x => x.Narcotic)
            .HasDefaultValue("no");

        modelBuilder.Entity<Product>()
            .Property(x => x.TaxType)
            .HasDefaultValue("exempt");

        modelBuilder.Entity<User>()
            .Property(x => x.Status)
            .HasDefaultValue("active");

        var dateConverter = new ValueConverter<DateTime, string>(
            v => v.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            s => ParseSqliteDateTime(s) ?? DateTime.MinValue);

        var nullableDateConverter = new ValueConverter<DateTime?, string?>(
            v => v.HasValue ? v.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
            s => ParseSqliteDateTime(s));

        var dateTimeConverter = new ValueConverter<DateTime, string>(
            v => v.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            s => ParseSqliteDateTime(s) ?? DateTime.MinValue);

        var nullableDateTimeConverter = new ValueConverter<DateTime?, string?>(
            v => v.HasValue ? v.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : null,
            s => ParseSqliteDateTime(s));

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            object? defaultInstance = null;
            try
            {
                defaultInstance = Activator.CreateInstance(clrType);
            }
            catch
            {
                // ignore if no parameterless constructor
            }

            foreach (var property in entityType.GetProperties())
            {
                var colName = property.GetColumnName() ?? property.Name;
                var isPureDate = PureDateColumnNames.Contains(colName);

                if (property.ClrType == typeof(DateTime))
                {
                    property.SetValueConverter(isPureDate ? dateConverter : dateTimeConverter);
                }
                else if (property.ClrType == typeof(DateTime?))
                {
                    property.SetValueConverter(isPureDate ? nullableDateConverter : nullableDateTimeConverter);
                }

                // Ensure SQLite DDL created via EnsureCreatedAsync() includes SQL DEFAULT clauses
                // for all non-primary-key value types and C# property initializers so raw SQL INSERTs
                // omitting optional columns never fail with NOT NULL constraint errors.
                if (!property.IsPrimaryKey() && !property.IsForeignKey() && !property.TryGetDefaultValue(out _) && property.GetDefaultValueSql() == null)
                {
                    if (defaultInstance != null && property.PropertyInfo != null)
                    {
                        var propVal = property.PropertyInfo.GetValue(defaultInstance);
                        if (propVal != null && property.ClrType == typeof(string))
                        {
                            property.SetDefaultValue(propVal);
                        }
                        else if (property.ClrType == typeof(bool))
                        {
                            property.SetDefaultValueSql((propVal is bool b && b) ? "1" : "0");
                        }
                        else if (property.ClrType == typeof(int) || property.ClrType == typeof(long) || property.ClrType == typeof(decimal) || property.ClrType == typeof(double))
                        {
                            var numStr = Convert.ToString(propVal ?? 0, CultureInfo.InvariantCulture) ?? "0";
                            property.SetDefaultValueSql(numStr);
                        }
                    }
                }
            }
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        StampTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void StampTimestamps()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            var createdAtProp = entry.Metadata.FindProperty("CreatedAt");
            if (entry.State == EntityState.Added && createdAtProp != null && entry.Property("CreatedAt").CurrentValue == null)
            {
                entry.Property("CreatedAt").CurrentValue = now;
            }

            var updatedAtProp = entry.Metadata.FindProperty("UpdatedAt");
            if (updatedAtProp != null)
            {
                entry.Property("UpdatedAt").CurrentValue = now;
            }
        }
    }

    public static DateTime? ParseSqliteDateTime(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        if (DateTime.TryParse(
                trimmed,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                out var parsed))
        {
            return parsed;
        }

        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixSeconds))
        {
            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
            }
            catch
            {
                return null;
            }
        }

        return null;
    }
}
