import React, { useEffect, useMemo, useState } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import { Link, useNavigate } from "react-router-dom";
import { ArrowLeftIcon, PlusCircleIcon, TagIcon } from "@heroicons/react/24/solid";
import BrandForm from "./BrandForm.jsx";
import { usePermissions } from "@/api/usePermissions.js";

export default function CreateBrand() {
  const navigate = useNavigate();
  const [saving, setSaving] = useState(false);
  const { loading: permsLoading, canFor } = usePermissions();
  const can = useMemo(
    () =>
      typeof canFor === "function"
        ? canFor("brand")
        : { view: false, create: false, update: false, delete: false },
    [canFor]
  );

  useEffect(() => {
    document.title = "Add Brand - Karobar App";
  }, []);

  const handleCreate = async (formData, rawForm) => {
    if (!can.create) {
      toast.error("You don't have permission to create brands.");
      return;
    }
    if (!(rawForm.name || "").trim()) {
      toast.error("Brand name is required.");
      return;
    }
    try {
      setSaving(true);
      await axios.post("/api/brands", formData, {
        headers: { "Content-Type": "multipart/form-data" },
      });
      toast.success("Brand created");
      navigate("/brands");
    } catch (err) {
      const msg =
        err?.response?.data?.message ||
        err?.response?.data?.errors?.name?.[0] ||
        "Failed to create brand";
      toast.error(msg);
    } finally {
      setSaving(false);
    }
  };

  if (permsLoading) return <div className="p-6">Loading…</div>;
  if (!can.create) {
    return (
      <div className="p-6 text-sm text-gray-700">
        You don&apos;t have permission to create brands.
      </div>
    );
  }

  return (
    <div className="product-form-page space-y-4">
      <div className="products-panel">
        <div className="products-heading">
          <div className="flex items-center gap-3 min-w-0">
            <div className="products-identity-icon">
              <TagIcon className="w-6 h-6" />
            </div>
            <div className="min-w-0">
              <h1 className="products-title">Add Brand</h1>
              <p className="products-subtitle">
                Create a brand and configure its Expiry Return Policy
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
              disabled={saving}
              className="products-action products-action-primary theme-btn-primary"
            >
              <PlusCircleIcon className="w-4 h-4" />
              <span>{saving ? "Saving..." : "Save Brand"}</span>
            </button>
          </div>
        </div>
      </div>

      <BrandForm onSubmit={handleCreate} saving={saving} submitLabel="Save Brand" />
    </div>
  );
}

