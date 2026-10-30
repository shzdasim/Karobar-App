import React, { useEffect, useMemo, useState } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import { Link, useNavigate, useParams } from "react-router-dom";
import { ArrowLeftIcon, PencilSquareIcon, TagIcon } from "@heroicons/react/24/solid";
import BrandForm from "./BrandForm.jsx";
import { ExpiryIntimationTable } from "@/components/ExpiryReturnPolicyFields.jsx";
import { usePermissions } from "@/api/usePermissions.js";
import PageSkeleton from "@/components/PageSkeleton.jsx";

export default function EditBrand() {
  const { id } = useParams();
  const navigate = useNavigate();
  const [brand, setBrand] = useState(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [fetchErr, setFetchErr] = useState(null);

  const { loading: permsLoading, canFor } = usePermissions();
  const can = useMemo(
    () =>
      typeof canFor === "function"
        ? canFor("brand")
        : { view: false, create: false, update: false, delete: false },
    [canFor]
  );

  useEffect(() => {
    document.title = "Edit Brand - Karobar App";
  }, []);

  useEffect(() => {
    if (permsLoading || !can.view) return;
    (async () => {
      try {
        setLoading(true);
        const { data } = await axios.get(`/api/brands/${id}`);
        setBrand(data);
        setFetchErr(null);
      } catch (err) {
        if (err?.response?.status === 404) setFetchErr("Brand not found.");
        else setFetchErr("Failed to load brand details.");
      } finally {
        setLoading(false);
      }
    })();
  }, [id, permsLoading, can.view]);

  const handleUpdate = async (formData, rawForm) => {
    if (!can.update) {
      toast.error("You don't have permission to update brands.");
      return;
    }
    if (!(rawForm.name || "").trim()) {
      toast.error("Brand name is required.");
      return;
    }
    try {
      setSaving(true);
      formData.append("_method", "PUT");
      await axios.post(`/api/brands/${id}`, formData, {
        headers: { "Content-Type": "multipart/form-data" },
      });
      toast.success("Brand updated");
      navigate("/brands");
    } catch (err) {
      const msg =
        err?.response?.data?.message ||
        err?.response?.data?.errors?.name?.[0] ||
        "Failed to update brand";
      toast.error(msg);
    } finally {
      setSaving(false);
    }
  };

  if (permsLoading || loading) return <PageSkeleton />;
  if (!can.view) {
    return (
      <div className="p-6 text-sm text-gray-700">
        You don&apos;t have permission to view brands.
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
              <TagIcon className="w-6 h-6" />
            </div>
            <div className="min-w-0">
              <h1 className="products-title">Edit Brand — {brand?.name}</h1>
              <p className="products-subtitle">
                Update brand details and Expiry Return Policy
              </p>
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            <Link to="/brands" className="products-action theme-btn-secondary">
              <ArrowLeftIcon className="w-3.5 h-3.5" />
              <span>Back to Brands</span>
            </Link>

            <button
              type="submit"
              form="brand-form"
              disabled={saving || !can.update}
              className="products-action products-action-primary theme-btn-primary"
            >
              <PencilSquareIcon className="w-4 h-4" />
              <span>{saving ? "Updating..." : "Update Brand"}</span>
            </button>
          </div>
        </div>
      </div>

      <BrandForm
        initialData={brand}
        onSubmit={handleUpdate}
        saving={saving}
        submitLabel="Update Brand"
      />

      {/* Live Expiry Return Intimation Status for Batches of this Brand */}
      <ExpiryIntimationTable
        batches={brand?.intimation_batches || []}
        entityLabel="Brand"
        policy={brand?.expiry_return_policy}
        months={brand?.expiry_return_months}
        days={brand?.expiry_return_days}
      />
    </div>
  );
}

