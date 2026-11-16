import React, { useEffect, useState } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import { Link, useNavigate } from "react-router-dom";
import {
  ArrowLeftIcon,
  PlusCircleIcon,
  IdentificationIcon,
} from "@heroicons/react/24/solid";
import DoctorForm from "./DoctorForm.jsx";

export default function CreateDoctor() {
  const navigate = useNavigate();
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    document.title = "Add Doctor - Karobar App";
  }, []);

  const handleCreate = async (payload) => {
    if (!(payload.name || "").trim()) {
      toast.error("Doctor name is required.");
      return;
    }
    try {
      setSaving(true);
      await axios.post("/api/doctors", payload);
      toast.success("Doctor added to directory");
      navigate("/doctors");
    } catch (err) {
      const msg =
        err?.response?.data?.message ||
        err?.response?.data?.errors?.name?.[0] ||
        "Failed to create doctor";
      toast.error(msg);
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="product-form-page space-y-4">
      <div className="products-panel">
        <div className="products-heading">
          <div className="flex items-center gap-3 min-w-0">
            <div className="products-identity-icon">
              <IdentificationIcon className="w-6 h-6" />
            </div>
            <div className="min-w-0">
              <h1 className="products-title">Add Doctor (RMP)</h1>
              <p className="products-subtitle">
                Save prescribing doctor name, PM&amp;DC Reg #, hospital/clinic &amp; contact for Sale Invoices and Narcotics Registers
              </p>
            </div>
          </div>

          <div className="flex items-center gap-2 shrink-0">
            <Link to="/doctors" className="products-action theme-btn-secondary">
              <ArrowLeftIcon className="w-3.5 h-3.5" />
              <span>Back to Doctors</span>
            </Link>

            <button
              type="submit"
              form="doctor-form"
              disabled={saving}
              className="products-action products-action-primary theme-btn-primary"
            >
              <PlusCircleIcon className="w-4 h-4" />
              <span>{saving ? "Saving..." : "Save Doctor (Ctrl+S)"}</span>
            </button>
          </div>
        </div>
      </div>

      <DoctorForm
        onSubmit={handleCreate}
        onCancel={() => navigate("/doctors")}
        saving={saving}
        isEdit={false}
        submitLabel="Save Doctor"
      />
    </div>
  );
}
