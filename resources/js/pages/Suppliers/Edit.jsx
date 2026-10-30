import React, { useEffect, useMemo, useState } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import { Link, useNavigate, useParams } from "react-router-dom";
import {
  ArrowLeftIcon,
  PencilSquareIcon,
  BuildingStorefrontIcon,
} from "@heroicons/react/24/solid";
import SupplierForm from "./SupplierForm.jsx";
import { ExpiryIntimationTable } from "@/components/ExpiryReturnPolicyFields.jsx";
import { usePermissions } from "@/api/usePermissions.js";
import PageSkeleton from "@/components/PageSkeleton.jsx";

export default function EditSupplier() {
  const { id } = useParams();
  const navigate = useNavigate();
  const [supplier, setSupplier] = useState(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [fetchErr, setFetchErr] = useState(null);

  const { loading: permsLoading, canFor } = usePermissions();
  const can = useMemo(
    () =>
      typeof canFor === "function"
        ? canFor("supplier")
        : { view: false, create: false, update: false, delete: false },
    [canFor]
  );

  useEffect(() => {
    document.title = "Edit Supplier - Karobar App";
  }, []);

  useEffect(() => {
    if (permsLoading || !can.view) return;
    (async () => {
      try {
        setLoading(true);
        const { data } = await axios.get(`/api/suppliers/${id}`);
        setSupplier(data);
        setFetchErr(null);
      } catch (err) {
        if (err?.response?.status === 404) setFetchErr("Supplier not found.");
        else setFetchErr("Failed to load supplier details.");
      } finally {
        setLoading(false);
      }
    })();
  }, [id, permsLoading, can.view]);

  const handleUpdate = async (payload) => {
    if (!can.update) {
      toast.error("You don't have permission to update suppliers.");
      return;
    }
    if (!(payload.name || "").trim()) {
      toast.error("Supplier name is required.");
      return;
    }
    try {
      setSaving(true);
      await axios.put(`/api/suppliers/${id}`, payload);
      toast.success("Supplier updated");
      navigate("/suppliers");
    } catch (err) {
      toast.error(err?.response?.data?.message || "Failed to update supplier");
    } finally {
      setSaving(false);
    }
  };

  if (permsLoading || loading) return <PageSkeleton />;
  if (!can.view) {
    return (
      <div className="p-6 text-sm text-gray-700">
        You don&apos;t have permission to view suppliers.
      </div>
    );
  }
  if (fetchErr) return <div className="p-6 text-red-600">{fetchErr}</div>;

  return (
    <div className="product-form-page space-y-4">
      <div className="products-panel">
        <div className="products-heading">
          <div className="flex items-center gap-3 min-w-0">
            <div className="products-identity-icon">
              <BuildingStorefrontIcon className="w-6 h-6" />
            </div>
            <div className="min-w-0">
              <h1 className="products-title">Edit Supplier — {supplier?.name}</h1>
              <p className="products-subtitle">
                Update supplier details, credit terms & Expiry Return Policy
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
              disabled={saving || !can.update}
              className="products-action products-action-primary theme-btn-primary"
            >
              <PencilSquareIcon className="w-4 h-4" />
              <span>{saving ? "Updating..." : "Update Supplier"}</span>
            </button>
          </div>
        </div>
      </div>

      <SupplierForm
        initialData={supplier}
        onSubmit={handleUpdate}
        saving={saving}
        isEdit={true}
        submitLabel="Update Supplier"
      />

      {/* Live Expiry Return Intimation Status for Batches Supplied by this Supplier */}
      <ExpiryIntimationTable
        batches={supplier?.intimation_batches || []}
        entityLabel="Supplier"
        policy={supplier?.expiry_return_policy}
        months={supplier?.expiry_return_months}
        days={supplier?.expiry_return_days}
      />
    </div>
  );
}

