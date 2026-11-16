import React, { useEffect, useState } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import { Link, useNavigate, useParams } from "react-router-dom";
import {
  ArrowLeftIcon,
  PencilSquareIcon,
  IdentificationIcon,
} from "@heroicons/react/24/solid";
import DoctorForm from "./DoctorForm.jsx";
import PageSkeleton from "@/components/PageSkeleton.jsx";

export default function EditDoctor() {
  const { id } = useParams();
  const navigate = useNavigate();
  const [doctor, setDoctor] = useState(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [fetchErr, setFetchErr] = useState(null);

  useEffect(() => {
    document.title = "Edit Doctor - Karobar App";
  }, []);

  useEffect(() => {
    (async () => {
      try {
        setLoading(true);
        const { data } = await axios.get(`/api/doctors/${id}`);
        setDoctor(data);
        setFetchErr(null);
      } catch (err) {
        if (err?.response?.status === 404) setFetchErr("Doctor not found.");
        else setFetchErr("Failed to load doctor details.");
      } finally {
        setLoading(false);
      }
    })();
  }, [id]);

  const handleUpdate = async (payload) => {
    if (!(payload.name || "").trim()) {
      toast.error("Doctor name is required.");
      return;
    }
    try {
      setSaving(true);
      await axios.put(`/api/doctors/${id}`, payload);
      toast.success("Doctor updated");
      navigate("/doctors");
    } catch (err) {
      const msg =
        err?.response?.data?.message ||
        err?.response?.data?.errors?.name?.[0] ||
        "Failed to update doctor";
      toast.error(msg);
    } finally {
      setSaving(false);
    }
  };

  if (loading) return <PageSkeleton />;
  if (fetchErr) return <div className="p-6 text-red-600">{fetchErr}</div>;

  return (
    <div className="product-form-page space-y-4">
      <div className="products-panel">
        <div className="products-heading">
          <div className="flex items-center gap-3 min-w-0">
            <div className="products-identity-icon">
              <IdentificationIcon className="w-6 h-6" />
            </div>
            <div className="min-w-0">
              <h1 className="products-title">Edit Doctor — {doctor?.name}</h1>
              <p className="products-subtitle">
                Update prescribing doctor details, PM&amp;DC Reg # &amp; hospital/clinic
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
              <PencilSquareIcon className="w-4 h-4" />
              <span>{saving ? "Updating..." : "Update Doctor (Ctrl+S)"}</span>
            </button>
          </div>
        </div>
      </div>

      <DoctorForm
        initialData={doctor}
        onSubmit={handleUpdate}
        onCancel={() => navigate("/doctors")}
        saving={saving}
        isEdit={true}
        submitLabel="Update Doctor"
      />
    </div>
  );
}
