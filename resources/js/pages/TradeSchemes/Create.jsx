// resources/js/pages/TradeSchemes/Create.jsx
import React, { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import axios from "axios";
import toast from "react-hot-toast";
import TradeSchemeForm from "./TradeSchemeForm.jsx";
import { usePermissions } from "@/api/usePermissions.js";

export default function CreateTradeScheme() {
  const navigate = useNavigate();
  const [saving, setSaving] = useState(false);
  const { loading: permsLoading, canFor } = usePermissions();

  const can = typeof canFor === "function" ? canFor("sale-invoice") : { create: true };

  useEffect(() => {
    document.title = "Create Trade Scheme & Bonus Slabs - Karobar App";
  }, []);

  const handleCreate = async (payload) => {
    if (!permsLoading && can && !can.create) {
      toast.error("You don't have permission to create trade schemes.");
      return;
    }

    try {
      setSaving(true);
      await axios.post("/api/trade-schemes", payload);
      toast.success("Trade scheme created successfully!");
      navigate("/trade-schemes");
    } catch (err) {
      const msg =
        err?.response?.data?.message ||
        err?.response?.data?.errors?.name?.[0] ||
        "Failed to create trade scheme.";
      toast.error(msg);
    } finally {
      setSaving(false);
    }
  };

  if (permsLoading) {
    return (
      <div className="p-6 text-sm text-gray-500 dark:text-slate-400">
        Loading permissions...
      </div>
    );
  }

  return (
    <div className="trade-schemes-create-page max-w-7xl mx-auto px-4 py-2">
      <TradeSchemeForm onSubmit={handleCreate} saving={saving} isEdit={false} />
    </div>
  );
}
