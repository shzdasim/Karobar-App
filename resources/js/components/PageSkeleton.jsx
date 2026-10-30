import React from "react";
import { useLocation } from "react-router-dom";

function resolveSkeletonVariant(pathname = "", explicitVariant) {
  if (explicitVariant && explicitVariant !== "auto") {
    return explicitVariant;
  }
  const clean = (pathname || "").split("?")[0].split("#")[0].replace(/\/+$/, "") || "/";

  if (
    clean === "/" ||
    clean === "/forgot-password" ||
    clean === "/activate" ||
    clean.startsWith("/reset-password")
  ) {
    return "auth";
  }

  if (clean === "/sale-invoices/pos" || clean === "/sale-invoices/barcode-pos") {
    return "pos";
  }

  if (clean === "/dashboard") {
    return "dashboard";
  }

  if (
    /^\/(sale-invoices|wholesale-invoices|purchase-invoices|sale-returns|purchase-returns|purchase-orders|stock-adjustments|stock-disposals|user-demands)\/(create|[^/]+\/edit)/.test(
      clean
    )
  ) {
    return "invoice-form";
  }

  if (
    /^\/(products|suppliers|brands|users|roles|stocktake-sessions)\/(create|[^/]+\/edit)/.test(
      clean
    ) ||
    clean === "/settings" ||
    clean === "/profile"
  ) {
    return "form";
  }

  if (
    /^\/(sale-invoices|purchase-invoices|stock-adjustments|stocktake-sessions)\/[^/]+/.test(clean)
  ) {
    return "detail";
  }

  if (
    clean.startsWith("/reports/") ||
    clean === "/accounting" ||
    clean === "/supplier-ledger" ||
    clean === "/customer-ledger" ||
    clean === "/hr-payroll" ||
    clean === "/cash-drawer" ||
    clean === "/batch-expiry" ||
    clean === "/purchase-orders/forecast"
  ) {
    return "report";
  }

  return "table";
}

function Bone({ className = "" }) {
  return <div className={`kd-skeleton-bone rounded-lg ${className}`} />;
}

function DashboardSkeleton() {
  return (
    <div className="space-y-4 p-1">
      {/* Top header & date filter toolbar */}
      <div className="g-card rounded-2xl p-4 flex flex-wrap items-center justify-between gap-3 border border-slate-200/70 dark:border-slate-700/60">
        <div className="space-y-2">
          <Bone className="h-6 w-48" />
          <Bone className="h-3.5 w-72" />
        </div>
        <div className="flex items-center gap-2">
          <Bone className="h-9 w-28 rounded-xl" />
          <Bone className="h-9 w-28 rounded-xl" />
          <Bone className="h-9 w-24 rounded-xl" />
        </div>
      </div>

      {/* Primary KPI metric cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-4 gap-4">
        {[0, 1, 2, 3].map((i) => (
          <div
            key={i}
            className="g-card rounded-2xl p-4 space-y-3 border border-slate-200/70 dark:border-slate-700/60"
          >
            <div className="flex items-center justify-between">
              <Bone className="h-4 w-24" />
              <Bone className="h-9 w-9 rounded-xl" />
            </div>
            <Bone className="h-7 w-36" />
            <div className="flex items-center justify-between pt-1">
              <Bone className="h-3 w-20" />
              <Bone className="h-3 w-16" />
            </div>
          </div>
        ))}
      </div>

      {/* Secondary KPI strip */}
      <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-3">
        {[0, 1, 2, 3, 4, 5].map((i) => (
          <div
            key={i}
            className="g-card rounded-xl p-3 space-y-2 border border-slate-200/70 dark:border-slate-700/60"
          >
            <Bone className="h-3 w-20" />
            <Bone className="h-5 w-16" />
          </div>
        ))}
      </div>

      {/* Charts & tables grid */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        <div className="lg:col-span-2 g-card rounded-2xl p-4 space-y-4 border border-slate-200/70 dark:border-slate-700/60">
          <div className="flex items-center justify-between">
            <Bone className="h-5 w-44" />
            <Bone className="h-7 w-28 rounded-lg" />
          </div>
          <Bone className="h-64 w-full rounded-xl" />
        </div>
        <div className="g-card rounded-2xl p-4 space-y-4 border border-slate-200/70 dark:border-slate-700/60">
          <Bone className="h-5 w-40" />
          <div className="space-y-3 pt-1">
            {[0, 1, 2, 3, 4].map((r) => (
              <div key={r} className="space-y-1.5">
                <div className="flex justify-between">
                  <Bone className="h-3.5 w-28" />
                  <Bone className="h-3.5 w-16" />
                </div>
                <Bone className="h-2.5 w-full rounded-full" />
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}

function InvoiceFormSkeleton() {
  return (
    <div className="space-y-3 p-1">
      {/* Header metadata bar */}
      <div className="g-card rounded-2xl p-3.5 space-y-3 border border-slate-200/70 dark:border-slate-700/60">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex items-center gap-3">
            <Bone className="h-9 w-9 rounded-xl" />
            <div className="space-y-1.5">
              <Bone className="h-5 w-44" />
              <Bone className="h-3 w-32" />
            </div>
          </div>
          <div className="flex items-center gap-2">
            <Bone className="h-8 w-24 rounded-lg" />
            <Bone className="h-8 w-28 rounded-lg" />
          </div>
        </div>
        <div className="grid grid-cols-2 sm:grid-cols-4 lg:grid-cols-6 gap-2.5 pt-1">
          {[0, 1, 2, 3, 4, 5].map((i) => (
            <div key={i} className="space-y-1">
              <Bone className="h-3 w-16" />
              <Bone className="h-8 w-full rounded-lg" />
            </div>
          ))}
        </div>
      </div>

      {/* Line-items grid */}
      <div className="g-card rounded-2xl p-3 space-y-2.5 border border-slate-200/70 dark:border-slate-700/60">
        <div className="grid grid-cols-12 gap-2 pb-2 border-b border-slate-200/60 dark:border-slate-700/60">
          <Bone className="col-span-4 h-4" />
          <Bone className="col-span-2 h-4" />
          <Bone className="col-span-2 h-4" />
          <Bone className="col-span-2 h-4" />
          <Bone className="col-span-2 h-4" />
        </div>
        {[0, 1, 2, 3, 4, 5].map((r) => (
          <div key={r} className="grid grid-cols-12 gap-2 items-center py-1">
            <Bone className="col-span-4 h-8 rounded-lg" />
            <Bone className="col-span-2 h-8 rounded-lg" />
            <Bone className="col-span-2 h-8 rounded-lg" />
            <Bone className="col-span-2 h-8 rounded-lg" />
            <Bone className="col-span-2 h-8 rounded-lg" />
          </div>
        ))}
      </div>

      {/* Footer calculation & payment summary */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-3">
        <div className="lg:col-span-2 g-card rounded-2xl p-3.5 space-y-2.5 border border-slate-200/70 dark:border-slate-700/60">
          <Bone className="h-4 w-32" />
          <div className="grid grid-cols-2 sm:grid-cols-4 gap-2.5">
            {[0, 1, 2, 3].map((i) => (
              <Bone key={i} className="h-12 w-full rounded-xl" />
            ))}
          </div>
        </div>
        <div className="g-card rounded-2xl p-3.5 space-y-2.5 border border-slate-200/70 dark:border-slate-700/60">
          <div className="flex justify-between items-center">
            <Bone className="h-4 w-24" />
            <Bone className="h-6 w-28" />
          </div>
          <Bone className="h-9 w-full rounded-lg" />
          <Bone className="h-9 w-full rounded-xl" />
        </div>
      </div>
    </div>
  );
}

function FormSkeleton() {
  return (
    <div className="space-y-4 p-1 max-w-5xl mx-auto">
      <div className="g-card rounded-2xl p-4 flex items-center justify-between border border-slate-200/70 dark:border-slate-700/60">
        <div className="flex items-center gap-3">
          <Bone className="h-10 w-10 rounded-xl" />
          <div className="space-y-1.5">
            <Bone className="h-5 w-44" />
            <Bone className="h-3.5 w-64" />
          </div>
        </div>
        <div className="flex items-center gap-2">
          <Bone className="h-9 w-24 rounded-xl" />
          <Bone className="h-9 w-28 rounded-xl" />
        </div>
      </div>

      <div className="g-card rounded-2xl p-5 space-y-5 border border-slate-200/70 dark:border-slate-700/60">
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
          {[0, 1, 2, 3, 4, 5, 6, 7, 8].map((i) => (
            <div key={i} className="space-y-1.5">
              <Bone className="h-3.5 w-24" />
              <Bone className="h-9 w-full rounded-xl" />
            </div>
          ))}
        </div>
        <div className="pt-3 border-t border-slate-200/60 dark:border-slate-700/60 flex justify-end gap-2.5">
          <Bone className="h-9 w-24 rounded-xl" />
          <Bone className="h-9 w-32 rounded-xl" />
        </div>
      </div>
    </div>
  );
}

function DetailSkeleton() {
  return (
    <div className="space-y-4 p-1">
      <div className="g-card rounded-2xl p-4 flex flex-wrap items-center justify-between gap-3 border border-slate-200/70 dark:border-slate-700/60">
        <div className="space-y-2">
          <Bone className="h-6 w-52" />
          <Bone className="h-3.5 w-36" />
        </div>
        <div className="flex items-center gap-2">
          <Bone className="h-9 w-24 rounded-xl" />
          <Bone className="h-9 w-24 rounded-xl" />
          <Bone className="h-9 w-28 rounded-xl" />
        </div>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
        {[0, 1, 2].map((i) => (
          <div
            key={i}
            className="g-card rounded-2xl p-4 space-y-2 border border-slate-200/70 dark:border-slate-700/60"
          >
            <Bone className="h-3.5 w-24" />
            <Bone className="h-5 w-40" />
            <Bone className="h-3 w-32" />
          </div>
        ))}
      </div>

      <div className="g-card rounded-2xl p-4 space-y-3 border border-slate-200/70 dark:border-slate-700/60">
        <Bone className="h-8 w-full rounded-lg" />
        {[0, 1, 2, 3, 4].map((r) => (
          <Bone key={r} className="h-9 w-full rounded-lg" />
        ))}
      </div>
    </div>
  );
}

function ReportSkeleton() {
  return (
    <div className="space-y-4 p-1">
      {/* Report filter header */}
      <div className="g-card rounded-2xl p-4 space-y-3.5 border border-slate-200/70 dark:border-slate-700/60">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex items-center gap-3">
            <Bone className="h-10 w-10 rounded-xl" />
            <div className="space-y-1.5">
              <Bone className="h-5 w-52" />
              <Bone className="h-3.5 w-72" />
            </div>
          </div>
          <div className="flex items-center gap-2">
            <Bone className="h-9 w-28 rounded-xl" />
            <Bone className="h-9 w-28 rounded-xl" />
          </div>
        </div>
        <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 pt-1">
          {[0, 1, 2, 3].map((i) => (
            <Bone key={i} className="h-9 w-full rounded-xl" />
          ))}
        </div>
      </div>

      {/* KPI summary strip */}
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
        {[0, 1, 2, 3].map((i) => (
          <div
            key={i}
            className="g-card rounded-2xl p-3.5 space-y-2 border border-slate-200/70 dark:border-slate-700/60"
          >
            <Bone className="h-3.5 w-24" />
            <Bone className="h-6 w-32" />
          </div>
        ))}
      </div>

      {/* Report table */}
      <div className="g-card rounded-2xl p-4 space-y-2.5 border border-slate-200/70 dark:border-slate-700/60">
        <Bone className="h-9 w-full rounded-lg" />
        {[0, 1, 2, 3, 4, 5, 6].map((r) => (
          <div key={r} className="grid grid-cols-6 gap-3 py-1">
            <Bone className="h-7 col-span-2" />
            <Bone className="h-7 col-span-1" />
            <Bone className="h-7 col-span-1" />
            <Bone className="h-7 col-span-1" />
            <Bone className="h-7 col-span-1" />
          </div>
        ))}
      </div>
    </div>
  );
}

function TableSkeleton() {
  return (
    <div className="space-y-3.5 p-1">
      {/* Header & search toolbar */}
      <div className="g-card rounded-2xl p-4 space-y-3 border border-slate-200/70 dark:border-slate-700/60">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex items-center gap-3">
            <Bone className="h-10 w-10 rounded-xl" />
            <div className="space-y-1.5">
              <Bone className="h-5 w-44" />
              <Bone className="h-3.5 w-64" />
            </div>
          </div>
          <div className="flex items-center gap-2">
            <Bone className="h-9 w-28 rounded-xl" />
            <Bone className="h-9 w-32 rounded-xl" />
          </div>
        </div>
        <div className="flex flex-wrap items-center gap-2.5 pt-1">
          <Bone className="h-9 flex-1 min-w-[200px] rounded-xl" />
          <Bone className="h-9 w-36 rounded-xl" />
          <Bone className="h-9 w-28 rounded-xl" />
        </div>
      </div>

      {/* Data table card */}
      <div className="g-card rounded-2xl p-3.5 space-y-2.5 border border-slate-200/70 dark:border-slate-700/60">
        <div className="grid grid-cols-12 gap-3 pb-2 border-b border-slate-200/60 dark:border-slate-700/60">
          <Bone className="col-span-1 h-4" />
          <Bone className="col-span-4 h-4" />
          <Bone className="col-span-2 h-4" />
          <Bone className="col-span-2 h-4" />
          <Bone className="col-span-2 h-4" />
          <Bone className="col-span-1 h-4" />
        </div>
        {[0, 1, 2, 3, 4, 5, 6, 7].map((r) => (
          <div key={r} className="grid grid-cols-12 gap-3 items-center py-1.5">
            <Bone className="col-span-1 h-6" />
            <Bone className="col-span-4 h-6" />
            <Bone className="col-span-2 h-6" />
            <Bone className="col-span-2 h-6" />
            <Bone className="col-span-2 h-6" />
            <Bone className="col-span-1 h-6" />
          </div>
        ))}
        <div className="flex items-center justify-between pt-2 border-t border-slate-200/60 dark:border-slate-700/60">
          <Bone className="h-4 w-36" />
          <div className="flex items-center gap-1.5">
            <Bone className="h-8 w-16 rounded-lg" />
            <Bone className="h-8 w-8 rounded-lg" />
            <Bone className="h-8 w-16 rounded-lg" />
          </div>
        </div>
      </div>
    </div>
  );
}

function PosSkeleton() {
  return (
    <div className="min-h-screen bg-slate-50 dark:bg-slate-950 p-3 flex flex-col gap-3">
      {/* POS top bar */}
      <div className="g-card rounded-2xl px-4 py-2.5 flex items-center justify-between border border-slate-200/70 dark:border-slate-800">
        <div className="flex items-center gap-3">
          <Bone className="h-8 w-8 rounded-lg" />
          <Bone className="h-5 w-40" />
        </div>
        <div className="flex items-center gap-2">
          <Bone className="h-8 w-28 rounded-lg" />
          <Bone className="h-8 w-24 rounded-lg" />
        </div>
      </div>
      {/* POS main split view */}
      <div className="flex-1 grid grid-cols-1 lg:grid-cols-12 gap-3">
        <div className="lg:col-span-8 g-card rounded-2xl p-3.5 space-y-3 border border-slate-200/70 dark:border-slate-800">
          <Bone className="h-11 w-full rounded-xl" />
          <Bone className="h-8 w-full rounded-lg" />
          {[0, 1, 2, 3, 4, 5].map((r) => (
            <Bone key={r} className="h-10 w-full rounded-lg" />
          ))}
        </div>
        <div className="lg:col-span-4 g-card rounded-2xl p-3.5 space-y-3 border border-slate-200/70 dark:border-slate-800">
          <Bone className="h-12 w-full rounded-xl" />
          <div className="grid grid-cols-3 gap-2">
            <Bone className="h-9 rounded-lg" />
            <Bone className="h-9 rounded-lg" />
            <Bone className="h-9 rounded-lg" />
          </div>
          <Bone className="h-28 w-full rounded-xl" />
          <Bone className="h-12 w-full rounded-xl" />
        </div>
      </div>
    </div>
  );
}

function AuthSkeleton() {
  return (
    <div className="min-h-screen flex items-center justify-center bg-slate-50 dark:bg-slate-950 p-4">
      <div className="w-full max-w-md g-card rounded-3xl p-7 space-y-5 border border-slate-200/70 dark:border-slate-800">
        <div className="flex flex-col items-center space-y-2.5">
          <Bone className="h-14 w-14 rounded-2xl" />
          <Bone className="h-6 w-44" />
          <Bone className="h-3.5 w-60" />
        </div>
        <div className="space-y-3.5 pt-2">
          <div className="space-y-1.5">
            <Bone className="h-3.5 w-24" />
            <Bone className="h-10 w-full rounded-xl" />
          </div>
          <div className="space-y-1.5">
            <Bone className="h-3.5 w-24" />
            <Bone className="h-10 w-full rounded-xl" />
          </div>
          <Bone className="h-11 w-full rounded-xl mt-2" />
        </div>
      </div>
    </div>
  );
}

export default function PageSkeleton({ variant = "auto", fullScreen = false }) {
  const location = useLocation();
  const resolved = resolveSkeletonVariant(location?.pathname, variant);

  let body;
  switch (resolved) {
    case "auth":
      body = <AuthSkeleton />;
      break;
    case "pos":
      body = <PosSkeleton />;
      break;
    case "dashboard":
      body = <DashboardSkeleton />;
      break;
    case "invoice-form":
      body = <InvoiceFormSkeleton />;
      break;
    case "form":
      body = <FormSkeleton />;
      break;
    case "detail":
      body = <DetailSkeleton />;
      break;
    case "report":
      body = <ReportSkeleton />;
      break;
    default:
      body = <TableSkeleton />;
      break;
  }

  const isStandalone = resolved === "auth" || resolved === "pos";

  return (
    <div
      className="kd-page-skeleton"
      aria-busy="true"
    >
      <span className="sr-only">Loading page content…</span>
      <div aria-hidden="true">
        {fullScreen && !isStandalone ? (
          <div className="min-h-screen bg-slate-50/70 dark:bg-slate-950 p-3 sm:p-4 space-y-3">
            {/* Topbar skeleton when rendered outside DashboardLayout */}
            <div className="g-card rounded-2xl px-4 py-2.5 flex items-center justify-between border border-slate-200/70 dark:border-slate-800">
              <div className="flex items-center gap-3">
                <Bone className="h-8 w-8 rounded-lg" />
                <Bone className="h-5 w-40" />
              </div>
              <div className="flex items-center gap-2">
                <Bone className="h-8 w-8 rounded-lg" />
                <Bone className="h-8 w-8 rounded-lg" />
                <Bone className="h-8 w-24 rounded-lg" />
              </div>
            </div>
            {body}
          </div>
        ) : (
          body
        )}
      </div>
    </div>
  );
}

