using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using Dapper;
using FluentAssertions;
using Karobar.Core.Entities;
using Karobar.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Karobar.Tests;

public class Phase2DatabaseVerificationTests
{
    private static readonly string[] Expected64LaravelModels =
    [
        "Account", "AccountTransaction", "BackupLog", "BackupSchedule", "BarcodeTemplate",
        "Batch", "Brand", "CashDrawerSession", "CashDrawerTransaction", "Category",
        "ChartOfAccount", "CubeProviderUsage", "Customer", "CustomerLedger", "CustomerRegularItem",
        "CustomerWholeSalePrice", "Doctor", "DocumentHistory", "DocumentSequence", "Employee",
        "EmployeeAdvance", "EmployeeAttendance", "Expense", "ExpenseCategory", "Formulation",
        "InvoiceDraft", "JournalEntry", "JournalEntryLine", "License", "Payment",
        "PaymentAllocation", "PayrollItem", "PayrollRun", "PersonalAccessToken", "Product",
        "ProductSupplier", "PurchaseInvoice", "PurchaseInvoiceItem", "PurchaseOrder", "PurchaseOrderItem",
        "PurchaseReturn", "PurchaseReturnItem", "SaleInvoice", "SaleInvoiceItem", "SaleReturn",
        "SaleReturnItem", "Setting", "StockAdjustment", "StockAdjustmentItem", "StockDisposal",
        "StockDisposalItem", "StocktakeItem", "StocktakeSession", "StorageLocation", "Supplier",
        "SupplierLedger", "SupplierProductPosition", "TaxRate", "Terminal", "ThemeSetting",
        "TradeScheme", "TradeSchemeSlab", "User", "UserDemand"
    ];

    private static readonly string[] ExpectedSpatiePermissionModels =
    [
        "Role", "Permission", "ModelHasRole", "ModelHasPermission", "RoleHasPermission"
    ];

    [Fact]
    public void All_69_Plan_And_Laravel_Entities_Exist_And_Are_Registered_In_KarobarDbContext()
    {
        var entityAssembly = typeof(User).Assembly;
        var entityTypes = entityAssembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.Namespace == "Karobar.Core.Entities")
            .ToDictionary(t => t.Name, t => t);

        foreach (var modelName in Expected64LaravelModels.Concat(ExpectedSpatiePermissionModels))
        {
            entityTypes.Should().ContainKey(modelName, $"Entity '{modelName}' must exist in Karobar.Core.Entities");
            var entityType = entityTypes[modelName];
            entityType.GetCustomAttribute<TableAttribute>().Should().NotBeNull($"Entity '{modelName}' must have [Table] attribute");
        }

        var dbSetElementTypes = typeof(KarobarDbContext)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .Select(p => p.PropertyType.GetGenericArguments()[0].Name)
            .ToHashSet();

        foreach (var modelName in Expected64LaravelModels.Concat(ExpectedSpatiePermissionModels))
        {
            dbSetElementTypes.Should().Contain(modelName, $"Entity '{modelName}' must be registered as a DbSet<> in KarobarDbContext");
        }
    }

    [Fact]
    public async Task Existing_Database_Sqlite_Passes_100_Percent_Schema_And_DbSet_Verification()
    {
        var tempSqlite = Path.Combine(Path.GetTempPath(), $"karobar_qa_schema_{Guid.NewGuid():N}.sqlite");
        try
        {
            var defaultFactory = new DbConnectionFactory();
            if (File.Exists(defaultFactory.SqlitePath) && new FileInfo(defaultFactory.SqlitePath).Length > 0)
            {
                File.Copy(defaultFactory.SqlitePath, tempSqlite, overwrite: true);
            }
            else
            {
                var bootstrapFactory = new DbConnectionFactory(explicitSqlitePath: tempSqlite);
                var initBootstrapper = new DatabaseBootstrapper(bootstrapFactory);
                await initBootstrapper.EnsureSchemaReadyAsync();
            }

            var factory = new DbConnectionFactory(explicitSqlitePath: tempSqlite);
            File.Exists(factory.SqlitePath).Should().BeTrue($"database.sqlite should exist at {factory.SqlitePath}");

            var bootstrapper = new DatabaseBootstrapper(factory);
            var verification = await bootstrapper.VerifySchemaCompatibilityAsync();

            verification.MissingTables.Should().BeEmpty("Every entity [Table] must exist in database.sqlite");
            verification.MissingColumns.Should().BeEmpty("Every entity [Column] must exist in database.sqlite");
            verification.DbSetErrors.Should().BeEmpty("Every DbSet<T> must query cleanly against database.sqlite");
            verification.IsCompatible.Should().BeTrue();
            verification.VerifiedTableCount.Should().BeGreaterThanOrEqualTo(68);
            verification.VerifiedDbSetCount.Should().BeGreaterThanOrEqualTo(68);
        }
        finally
        {
            try { if (File.Exists(tempSqlite)) File.Delete(tempSqlite); } catch { /* ignore */ }
            try { if (File.Exists(tempSqlite + "-wal")) File.Delete(tempSqlite + "-wal"); } catch { /* ignore */ }
            try { if (File.Exists(tempSqlite + "-shm")) File.Delete(tempSqlite + "-shm"); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Sqlite_Pragmas_And_Isolated_Read_Write_Work_Cleanly()
    {
        var tempSqlite = Path.Combine(Path.GetTempPath(), $"karobar_qa_p2_{Guid.NewGuid():N}.sqlite");
        try
        {
            var sourceFactory = new DbConnectionFactory();
            if (File.Exists(sourceFactory.SqlitePath) && new FileInfo(sourceFactory.SqlitePath).Length > 0)
            {
                File.Copy(sourceFactory.SqlitePath, tempSqlite, overwrite: true);
            }
            else
            {
                var initFactory = new DbConnectionFactory(explicitSqlitePath: tempSqlite);
                var initBootstrapper = new DatabaseBootstrapper(initFactory);
                await initBootstrapper.EnsureSchemaReadyAsync();
            }

            var testFactory = new DbConnectionFactory(explicitSqlitePath: tempSqlite);
            await using var conn = await testFactory.OpenConnectionAsync();

            var journalMode = await conn.ExecuteScalarAsync<string>("PRAGMA journal_mode;");
            journalMode.Should().BeEquivalentTo("wal");

            var foreignKeys = await conn.ExecuteScalarAsync<int>("PRAGMA foreign_keys;");
            foreignKeys.Should().Be(1);

            var busyTimeout = await conn.ExecuteScalarAsync<int>("PRAGMA busy_timeout;");
            busyTimeout.Should().BeGreaterThanOrEqualTo(10000);

            await using var db = DatabaseBootstrapper.CreateDbContext(tempSqlite);
            var brand = new Brand
            {
                Name = $"QA Test Brand {Guid.NewGuid():N}",
                ExpiryReturnPolicy = "before_expiry",
                ExpiryReturnMonths = 6,
                ExpiryReturnDays = 180
            };
            db.Brands.Add(brand);
            await db.SaveChangesAsync();

            brand.Id.Should().BeGreaterThan(0);
            brand.CreatedAt.Should().NotBeNull();
            brand.UpdatedAt.Should().NotBeNull();

            var fetched = await db.Brands.FirstOrDefaultAsync(b => b.Id == brand.Id);
            fetched.Should().NotBeNull();
            fetched!.ExpiryPolicyLabel.Should().Be("6 Months Before Expiry");
        }
        finally
        {
            try { if (File.Exists(tempSqlite)) File.Delete(tempSqlite); } catch { /* ignore */ }
            try { if (File.Exists(tempSqlite + "-wal")) File.Delete(tempSqlite + "-wal"); } catch { /* ignore */ }
            try { if (File.Exists(tempSqlite + "-shm")) File.Delete(tempSqlite + "-shm"); } catch { /* ignore */ }
        }
    }
}
