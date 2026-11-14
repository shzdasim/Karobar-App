// resources/js/pages/TradeSchemes/Edit.jsx
import React, { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import axios from "axios";
import toast from "react-hot-toast";
import TradeSchemeForm from "./TradeSchemeForm.jsx";
import { usePermissions } from "@/api/usePermissions.js";

export default function EditTradeScheme() {
  const { id } = useParams();
  const navigate = useNavigate();
  const [scheme, setScheme] = useState(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const { loading: permsLoading, canFor } = usePermissions();

  const can = typeof canFor === "function" ? canFor("sale-invoice") : { update: true };

  useEffect(() => {
    document.title = "Edit Trade Scheme - Karobar App";
  }, []);

  useEffect(() => {
    if (!id) return;
    let isMounted = true;
    (async () => {
      try {
        setLoading(true);
        const { data } = await axios.get(`/api/trade-schemes/${id}`);
        if (isMounted) {
          setScheme(data);
        }
      } catch (err) {
        toast.error("Failed to load trade scheme.");
        navigate("/trade-schemes");
      } finally {
        if (isMounted) setLoading(false);
      }
    })();
    return () => {
      isMounted = false;
    };
  }, [id, navigate]);

  const handleUpdate = async (payload) => {
    if (!permsLoading && can && !can.update) {
      toast.error("You don't have permission to update trade schemes.");
      return;
    }

    try {
      setSaving(true);
      await axios.put(`/api/trade-schemes/${id}`, payload);
      toast.success("Trade scheme updated successfully!");
      navigate("/trade-schemes");
    } catch (err) {
      const msg =
        err?.response?.data?.message ||
        err?.response?.data?.errors?.name?.[0] ||
        "Failed to update trade scheme.";
      toast.error(msg);
    } finally {
      setSaving(false);
    }
  };

  if (loading || permsLoading) {
    return (
      <div className="p-6 text-sm text-gray-500 dark:text-slate-400">
        Loading trade scheme details...
      </div>
    );
  }

  if (!scheme) {
    return (
      <div className="p-6 text-sm text-red-500">
        Trade scheme not found.
      </div>
    );
  }

  return (
    <div className="trade-schemes-edit-page max-w-7xl mx-auto px-4 py-2">
      <TradeSchemeForm
        initialData={scheme}
        onSubmit={handleUpdate}
        saving={saving}
        isEdit={true}
      />
    </div>
  );
}
