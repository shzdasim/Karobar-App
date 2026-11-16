<?php

namespace App\Http\Controllers;

use App\Models\Doctor;
use Illuminate\Database\Schema\Blueprint;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Schema;

class DoctorController extends Controller
{
    protected static bool $schemaChecked = false;

    public static function ensureDoctorsSchema(): void
    {
        if (self::$schemaChecked && Schema::hasTable('doctors')) {
            return;
        }

        try {
            if (!Schema::hasTable('doctors')) {
                Schema::create('doctors', function (Blueprint $table) {
                    $table->id();
                    $table->string('name');
                    $table->string('pmdc_number', 100)->nullable()->index();
                    $table->string('hospital_clinic', 255)->nullable();
                    $table->string('specialization', 191)->nullable();
                    $table->string('phone', 60)->nullable();
                    $table->string('address', 500)->nullable();
                    $table->boolean('is_active')->default(true)->index();
                    $table->timestamps();
                });
            }
            self::$schemaChecked = true;
        } catch (\Throwable) {
            // Ignore in read-only environments
        }
    }

    public function search(Request $request)
    {
        self::ensureDoctorsSchema();

        $q     = trim((string) $request->query('q', ''));
        $page  = max(1, (int) $request->query('page', 1));
        $limit = min(50, max(5, (int) $request->query('per_page', $request->query('limit', 20))));

        $builder = Doctor::query()->where('is_active', true);

        if ($q !== '') {
            $builder->where(function ($w) use ($q) {
                $w->where('name', 'like', "%{$q}%")
                  ->orWhere('pmdc_number', 'like', "%{$q}%")
                  ->orWhere('hospital_clinic', 'like', "%{$q}%")
                  ->orWhere('specialization', 'like', "%{$q}%")
                  ->orWhere('phone', 'like', "%{$q}%");
            });
        }

        $builder->orderBy('name');

        $paginator = $builder->paginate($limit, ['*'], 'page', $page);

        return response()->json([
            'data'      => $paginator->items(),
            'last_page' => $paginator->lastPage(),
            'next_page' => $paginator->hasMorePages() ? ($paginator->currentPage() + 1) : null,
        ]);
    }

    public function index(Request $request)
    {
        self::ensureDoctorsSchema();

        $q = trim((string) $request->query('q', ''));

        $builder = Doctor::query()->select('doctors.*');

        if (Schema::hasTable('sale_invoices') && Schema::hasColumn('sale_invoices', 'doctor_id')) {
            $builder->selectSub(
                'SELECT COUNT(*) FROM sale_invoices si WHERE si.doctor_id = doctors.id',
                'prescriptions_count'
            );
        }

        if ($q !== '') {
            $builder->where(function ($w) use ($q) {
                $w->where('name', 'like', "%{$q}%")
                  ->orWhere('pmdc_number', 'like', "%{$q}%")
                  ->orWhere('hospital_clinic', 'like', "%{$q}%")
                  ->orWhere('specialization', 'like', "%{$q}%")
                  ->orWhere('phone', 'like', "%{$q}%");
            });
        }

        $doctors = $builder->orderBy('name')->get();

        return response()->json($doctors);
    }

    private function authorizeDoctorMutation(Request $request, array $allowedPermissions): void
    {
        $user = $request->user();
        if (!$user) {
            abort(401);
        }
        if ($user->hasRole(['Admin', 'admin', 'super-admin'])) {
            return;
        }
        foreach ($allowedPermissions as $perm) {
            if ($user->can($perm)) {
                return;
            }
        }
        abort(403, 'You do not have permission to modify doctors.');
    }

    public function store(Request $request)
    {
        $this->authorizeDoctorMutation($request, ['customer.create', 'sale-invoice.create', 'sale-invoice.update']);
        self::ensureDoctorsSchema();

        $validated = $request->validate([
            'name'            => 'required|string|max:191',
            'pmdc_number'     => 'nullable|string|max:100',
            'hospital_clinic' => 'nullable|string|max:255',
            'specialization'  => 'nullable|string|max:191',
            'phone'           => 'nullable|string|max:60',
            'address'         => 'nullable|string|max:500',
            'is_active'       => 'nullable|boolean',
        ]);

        $validated['name'] = trim($validated['name']);
        $validated['is_active'] = array_key_exists('is_active', $validated) ? (bool) $validated['is_active'] : true;

        $doctor = Doctor::create($validated);

        return response()->json($doctor, 201);
    }

    public function show(Doctor $doctor)
    {
        self::ensureDoctorsSchema();
        return response()->json($doctor);
    }

    public function update(Request $request, Doctor $doctor)
    {
        $this->authorizeDoctorMutation($request, ['customer.update', 'sale-invoice.create', 'sale-invoice.update']);
        self::ensureDoctorsSchema();

        $validated = $request->validate([
            'name'            => 'required|string|max:191',
            'pmdc_number'     => 'nullable|string|max:100',
            'hospital_clinic' => 'nullable|string|max:255',
            'specialization'  => 'nullable|string|max:191',
            'phone'           => 'nullable|string|max:60',
            'address'         => 'nullable|string|max:500',
            'is_active'       => 'nullable|boolean',
        ]);

        $validated['name'] = trim($validated['name']);

        $doctor->update($validated);

        // Also keep linked sale_invoices' denormalized doctor fields synced if needed
        if (Schema::hasTable('sale_invoices') && Schema::hasColumn('sale_invoices', 'doctor_id')) {
            DB::table('sale_invoices')
                ->where('doctor_id', $doctor->id)
                ->update([
                    'doctor_name'     => $doctor->name,
                    'doctor_pmdc'     => $doctor->pmdc_number,
                    'doctor_hospital' => $doctor->hospital_clinic,
                ]);
        }

        return response()->json($doctor);
    }

    public function destroy(Request $request, Doctor $doctor)
    {
        $this->authorizeDoctorMutation($request, ['customer.delete', 'sale-invoice.delete']);
        self::ensureDoctorsSchema();

        if (Schema::hasTable('sale_invoices') && Schema::hasColumn('sale_invoices', 'doctor_id')) {
            DB::table('sale_invoices')
                ->where('doctor_id', $doctor->id)
                ->update(['doctor_id' => null]);
        }

        $doctor->delete();

        return response()->json(null, 204);
    }
}
