import React, { useEffect, useMemo, useState } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import { Link, useNavigate } from "react-router-dom";
import {
  ArrowLeftIcon,
  PlusCircleIcon,
  BuildingStorefrontIcon,
} from "@heroicons/react/24/solid";
import SupplierForm from "./SupplierForm.jsx";
import { usePermissions } from "@/api/usePermissions.js";

export default function CreateSupplier() {
  const navigate = useNavigate();
  const [saving, setSaving] = useState(false);
  const { loading: permsLoading, canFor } = usePermissions();
  const can = useMemo(
    () =>
      typeof canFor === "function"
        ? canFor("supplier")
        : { view: false, create: false, update: false, delete: false },
    [canFor]
  );

  useEffect(() => {
    document.title = "Add Supplier - Karobar App";
  }, []);

  const handleCreate = async (payload) => {
    if (!can.create) {
      toast.error("You don't have permission to create suppliers.");
      return;
    }
    if (!(payload.name || "").trim()) {
      toast.error("Supplier name is required.");
      return;
    }
    try {
      setSaving(true);
      await axios.post("/api/suppliers", payload);
      toast.success("Supplier created");
      navigate("/suppliers");
    } catch (err) {
      toast.error(err?.response?.data?.message || "Failed to create supplier");
    } finally {
      setSaving(false);
    }
  };

  if (permsLoading) return <div className="p-6">Loading…</div>;
  if (!can.create) {
    return (
      <div className="p-6 text-sm text-gray-700">
        You don&apos;t have permission to create suppliers.
      </div>
    );
  }

  return (
    <div className="product-form-page space-y-4">
      <div className="products-panel">
        <div className="products-heading">
          <div className="flex items-center gap-3 min-w-0">
            <div className="products-identity-icon">
              <BuildingStorefrontIcon className="w-6 h-6" />
            </div>
            <div className="min-w-0">
              <h1 className="products-title">Add Supplier</h1>
              <p className="products-subtitle">
                Create a supplier and configure credit terms & Expiry Return Policy
              </p>
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            <Link to="/suppliers" className="products-action theme-btn-secondary">
              <ArrowLeftIcon className="w-3.5 h-3.5" />
              <span>Back to Suppliers</span>
            </Link>

            <button
              type="submit"
              form="supplier-form"
              disabled={saving}
              className="products-action products-action-primary theme-btn-primary"
            >
              <PlusCircleIcon className="w-4 h-4" />
              <span>{saving ? "Saving..." : "Save Supplier"}</span>
            </button>
          </div>
        </div>
      </div>

      <SupplierForm
        onSubmit={handleCreate}
        saving={saving}
        isEdit={false}
        submitLabel="Save Supplier"
      />
    </div>
  );
}

