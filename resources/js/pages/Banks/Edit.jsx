import React, { useEffect, useMemo, useState } from "react";
// Image upload is handled with native file input to avoid FilePond validation issues


import axios from "axios";
import toast from "react-hot-toast";
import { useNavigate, useParams } from "react-router-dom";
import { usePermissions } from "@/api/usePermissions.js";

import { BanknotesIcon, ArrowUturnLeftIcon } from "@heroicons/react/24/solid";

export default function BanksEdit() {
  const { id } = useParams();
  const navigate = useNavigate();

  const { loading: permsLoading, canFor } = usePermissions();
  const can = useMemo(
    () =>
      (typeof canFor === "function"
        ? canFor("bank")
        : { view: false, create: false, update: false, delete: false }),
    [canFor]
  );

  const [loading, setLoading] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  const [form, setForm] = useState({
    bank_name: "",
    account_number: "",
    balance: 0,
    image: null,
  });

  const setField = (k, v) => setForm((prev) => ({ ...prev, [k]: v }));

  useEffect(() => {
    const load = async () => {
      if (permsLoading) return;
      if (!can?.view && !can?.update) return;
      try {
        setLoading(true);
        const { data } = await axios.get(`/api/banks/${id}`);
        const b = data?.data || data;
        setForm({
          bank_name: b.bank_name || "",
          account_number: b.account_number || "",
          balance: b.balance ?? 0,
          // FilePond needs a concrete source (URL) to preview existing image
          image: b.image_path
            ? `${window.location.origin}/storage/${b.image_path}`
            : b.image
              ? b.image
              : null,
        });
      } catch (e) {
        toast.error(e?.response?.data?.message || "Failed to load bank");
      } finally {
        setLoading(false);
      }
    };
    load();
  }, [id, permsLoading, can?.view, can?.update]);

  const submit = async (e) => {
    e.preventDefault();
    if (!can?.update) return toast.error("No permission");

    try {
      setSubmitting(true);
      const fd = new FormData();
      fd.append("bank_name", form.bank_name);
      fd.append("account_number", form.account_number);
      fd.append("balance", String(form.balance ?? 0));

      // Append `image` only when a real File was selected.
      if (form.image && typeof form.image !== "string") {
        if (form.image.size > 0) {
          fd.append("image", form.image);
        } else {
          toast.error("Selected image file is empty.");
          return;
        }
      }

      await axios.put(`/api/banks/${id}`, fd, {
        headers: { "Content-Type": "multipart/form-data" },
      });

      toast.success("Bank updated");
      navigate("/banks");
    } catch (e2) {
      toast.error(e2?.response?.data?.message || "Update failed");
    } finally {
      setSubmitting(false);
    }
  };

  if (permsLoading) return <div className="p-6">Loading…</div>;
  if (!can?.view && !can?.update) return <div className="p-6 text-sm text-gray-700">You don't have permission.</div>;

  return (
    <div className="p-4">
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm">
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100 dark:border-slate-700">
          <div className="flex items-center gap-3">
            <div className="p-2 rounded-lg shadow-sm" style={{ background: "linear-gradient(to bottom right, #3b82f6, #2563eb)" }}>
              <BanknotesIcon className="w-5 h-5 text-white" />
            </div>
            <div>
              <h1 className="text-lg font-semibold text-gray-900 dark:text-white">Edit Bank</h1>
              <p className="text-xs text-gray-500 dark:text-gray-400">Update bank account</p>
            </div>
          </div>

          <button
            onClick={() => navigate("/banks")}
            className="inline-flex items-center gap-2 rounded-md px-3 py-1.5 text-sm font-medium bg-white/60 dark:bg-slate-700/60 border border-gray-200/60 dark:border-slate-600/40"
          >
            <ArrowUturnLeftIcon className="w-4 h-4" />
            Back
          </button>
        </div>

        <form onSubmit={submit} className="p-4 space-y-4">
          {loading ? <div className="text-sm text-gray-600">Loading bank…</div> : null}

          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <label className="block">
              <span className="text-sm font-medium text-gray-700 dark:text-gray-200">Bank name</span>
              <input
                value={form.bank_name}
                onChange={(e) => setField("bank_name", e.target.value)}
                required
                className="mt-1 w-full h-10 px-3 rounded border border-gray-300 dark:border-slate-600 bg-white dark:bg-slate-900 text-sm"
              />
            </label>

            <label className="block">
              <span className="text-sm font-medium text-gray-700 dark:text-gray-200">Account number</span>
              <input
                value={form.account_number}
                onChange={(e) => setField("account_number", e.target.value)}
                required
                className="mt-1 w-full h-10 px-3 rounded border border-gray-300 dark:border-slate-600 bg-white dark:bg-slate-900 text-sm"
              />
            </label>
          </div>

          <label className="block">
            <span className="text-sm font-medium text-gray-700 dark:text-gray-200">Balance</span>
            <input
              type="number"
              value={form.balance}
              readOnly
              disabled
              className="mt-1 w-full h-10 px-3 rounded border border-gray-200 dark:border-slate-700 bg-gray-100 dark:bg-slate-900 text-sm text-gray-500 dark:text-slate-400"
            />
          </label>

          <label className="block">
            <span className="text-sm font-medium text-gray-700 dark:text-gray-200">Bank image (optional)</span>

            {typeof form.image === "string" && form.image && (
              <div className="mt-2 mb-2">
                <img
                  src={form.image}
                  alt="Bank"
                  className="h-24 w-24 object-cover rounded border border-gray-200 dark:border-slate-700"
                />
              </div>
            )}

            <input
              type="file"
              accept="image/*"
              className="mt-1 w-full text-sm"
              onChange={(e) => {
                const file = e.target.files?.[0] || null;
                setField("image", file);
              }}
            />

            {form.image && typeof form.image !== "string" && (
              <div className="mt-1 text-xs text-gray-500">
                Selected: {form.image?.name}
              </div>
            )}
          </label>


          <div className="flex items-center justify-end gap-2 pt-2">
            <button
              type="button"
              onClick={() => navigate("/banks")}
              className="rounded-md px-4 py-2 text-sm font-medium bg-white/60 dark:bg-slate-700/60 border border-gray-200/60 dark:border-slate-600/40"
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={submitting || !can?.update}
              className="rounded-md px-4 py-2 text-sm font-semibold bg-blue-600 text-white disabled:opacity-60"
            >
              {submitting ? "Saving…" : "Save"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

