using Dapper;
using Karobar.Api.Infrastructure;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/doctors")]
public class DoctorController : ApiControllerBase
{
    private static bool _schemaChecked;

    private static readonly string[] ViewPermissions =
    [
        "doctor.view", "customer.view", "sale-invoice.view", "sale-invoice.create",
        "sale-invoice.update", "barcode-pos.view", "barcode-pos.create"
    ];

    public DoctorController(KarobarDb db) : base(db)
    {
    }

    private async Task EnsureDoctorsSchemaAsync()
    {
        if (_schemaChecked && await Db.HasTableAsync("doctors")) return;

        try
        {
            if (!await Db.HasTableAsync("doctors"))
            {
                await Db.ExecuteAsync(@"
                    CREATE TABLE IF NOT EXISTS doctors (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        name VARCHAR(255) NOT NULL,
                        pmdc_number VARCHAR(100) NULL,
                        hospital_clinic VARCHAR(255) NULL,
                        specialization VARCHAR(191) NULL,
                        phone VARCHAR(60) NULL,
                        address VARCHAR(500) NULL,
                        is_active TINYINT(1) NOT NULL DEFAULT 1,
                        created_at DATETIME NULL,
                        updated_at DATETIME NULL
                    );");
                KarobarDb.FlushSchemaCache();
            }
            _schemaChecked = true;
        }
        catch
        {
        }
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string? q = "",
        [FromQuery] int page = 1,
        [FromQuery(Name = "per_page")] int? perPage = null,
        [FromQuery] int? limit = null)
    {
        if (!await CanAsync(ViewPermissions)) return Forbidden("You do not have permission to view doctors.");
        await EnsureDoctorsSchemaAsync();

        q = (q ?? "").Trim();
        page = Math.Max(1, page);
        var effectiveLimit = Math.Clamp(perPage ?? limit ?? 20, 5, 50);

        var where = "WHERE is_active = 1";
        var param = new DynamicParameters();
        if (!string.IsNullOrEmpty(q))
        {
            where += " AND (name LIKE @q OR pmdc_number LIKE @q OR hospital_clinic LIKE @q OR specialization LIKE @q OR phone LIKE @q)";
            param.Add("q", $"%{q}%");
        }

        var paged = await Db.PaginateAsync(
            $"SELECT * FROM doctors {where} ORDER BY name ASC",
            $"SELECT COUNT(1) FROM doctors {where}",
            param,
            page,
            effectiveLimit);

        var lastPage = Convert.ToInt32(paged["last_page"] ?? 1);
        var currentPage = Convert.ToInt32(paged["current_page"] ?? 1);

        return Ok(new Dictionary<string, object?>
        {
            ["data"] = paged["data"],
            ["last_page"] = lastPage,
            ["next_page"] = currentPage < lastPage ? currentPage + 1 : null
        });
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] string? q = "")
    {
        if (!await CanAsync(ViewPermissions)) return Forbidden("You do not have permission to view doctors.");
        await EnsureDoctorsSchemaAsync();

        q = (q ?? "").Trim();
        var hasDoctorIdOnSales = await Db.HasTableAsync("sale_invoices") && await Db.HasColumnAsync("sale_invoices", "doctor_id");

        var selectCols = hasDoctorIdOnSales
            ? "d.*, (SELECT COUNT(1) FROM sale_invoices si WHERE si.doctor_id = d.id) AS prescriptions_count"
            : "d.*";

        var sql = $"SELECT {selectCols} FROM doctors d";
        var param = new DynamicParameters();
        if (!string.IsNullOrEmpty(q))
        {
            sql += " WHERE (d.name LIKE @q OR d.pmdc_number LIKE @q OR d.hospital_clinic LIKE @q OR d.specialization LIKE @q OR d.phone LIKE @q)";
            param.Add("q", $"%{q}%");
        }
        sql += " ORDER BY d.name ASC";

        var doctors = await Db.QueryListAsync(sql, param);
        return Ok(doctors);
    }

    [HttpPost]
    public async Task<IActionResult> Store()
    {
        if (!await CanAsync("doctor.create", "customer.create", "sale-invoice.create", "sale-invoice.update", "barcode-pos.create"))
        {
            return Forbidden("You do not have permission to modify doctors.");
        }
        await EnsureDoctorsSchemaAsync();

        var body = await ReadJsonBodyAsync();
        var name = GetString(body, "name")?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
        {
            return Unprocessable("The name field is required.", new Dictionary<string, string[]>
            {
                ["name"] = ["The name field is required."]
            });
        }

        var data = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["pmdc_number"] = GetString(body, "pmdc_number"),
            ["hospital_clinic"] = GetString(body, "hospital_clinic"),
            ["specialization"] = GetString(body, "specialization"),
            ["phone"] = GetString(body, "phone"),
            ["address"] = GetString(body, "address"),
            ["is_active"] = GetBool(body, "is_active", true)
        };

        var id = await Db.InsertAsync("doctors", data);
        var doctor = await Db.QuerySingleOrDefaultAsync("SELECT * FROM doctors WHERE id = @id", new { id });
        return StatusCode(201, doctor);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Show(long id)
    {
        if (!await CanAsync(ViewPermissions)) return Forbidden("You do not have permission to view doctors.");
        await EnsureDoctorsSchemaAsync();

        var doctor = await Db.QuerySingleOrDefaultAsync("SELECT * FROM doctors WHERE id = @id", new { id });
        if (doctor == null) return NotFound(new { message = "Doctor not found." });

        return Ok(doctor);
    }

    [HttpPut("{id:long}")]
    [HttpPatch("{id:long}")]
    public async Task<IActionResult> Update(long id)
    {
        if (!await CanAsync("doctor.update", "customer.update", "sale-invoice.create", "sale-invoice.update"))
        {
            return Forbidden("You do not have permission to modify doctors.");
        }
        await EnsureDoctorsSchemaAsync();

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM doctors WHERE id = @id", new { id });
        if (existing == null) return NotFound(new { message = "Doctor not found." });

        var body = await ReadJsonBodyAsync();
        var name = GetString(body, "name")?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
        {
            return Unprocessable("The name field is required.", new Dictionary<string, string[]>
            {
                ["name"] = ["The name field is required."]
            });
        }

        var updateData = new Dictionary<string, object?>
        {
            ["name"] = name
        };
        if (body.ContainsKey("pmdc_number")) updateData["pmdc_number"] = GetString(body, "pmdc_number");
        if (body.ContainsKey("hospital_clinic")) updateData["hospital_clinic"] = GetString(body, "hospital_clinic");
        if (body.ContainsKey("specialization")) updateData["specialization"] = GetString(body, "specialization");
        if (body.ContainsKey("phone")) updateData["phone"] = GetString(body, "phone");
        if (body.ContainsKey("address")) updateData["address"] = GetString(body, "address");
        if (body.ContainsKey("is_active")) updateData["is_active"] = GetBool(body, "is_active", true);

        await Db.UpdateAsync("doctors", id, updateData);
        var doctor = await Db.QuerySingleOrDefaultAsync("SELECT * FROM doctors WHERE id = @id", new { id });

        if (doctor != null && await Db.HasTableAsync("sale_invoices") && await Db.HasColumnAsync("sale_invoices", "doctor_id"))
        {
            await Db.ExecuteAsync(@"
                UPDATE sale_invoices
                SET doctor_name = @docName, doctor_pmdc = @docPmdc, doctor_hospital = @docHosp
                WHERE doctor_id = @id",
                new
                {
                    docName = doctor["name"],
                    docPmdc = doctor["pmdc_number"],
                    docHosp = doctor["hospital_clinic"],
                    id
                });
        }

        return Ok(doctor);
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Destroy(long id)
    {
        if (!await CanAsync("doctor.delete", "customer.delete", "sale-invoice.delete"))
        {
            return Forbidden("You do not have permission to modify doctors.");
        }
        await EnsureDoctorsSchemaAsync();

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM doctors WHERE id = @id", new { id });
        if (existing == null) return NotFound(new { message = "Doctor not found." });

        if (await Db.HasTableAsync("sale_invoices") && await Db.HasColumnAsync("sale_invoices", "doctor_id"))
        {
            await Db.ExecuteAsync("UPDATE sale_invoices SET doctor_id = NULL WHERE doctor_id = @id", new { id });
        }

        await Db.ExecuteAsync("DELETE FROM doctors WHERE id = @id", new { id });
        return NoContent();
    }
}
