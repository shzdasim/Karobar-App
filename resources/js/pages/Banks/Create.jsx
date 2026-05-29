import React, { useMemo, useState } from "react";
import { FilePond, registerPlugin } from "react-filepond";
import FilePondPluginImagePreview from "filepond-plugin-image-preview";
import "filepond/dist/filepond.min.css";
import "filepond-plugin-image-preview/dist/filepond-plugin-image-preview.css";

registerPlugin(FilePondPluginImagePreview);

import axios from "axios";
import toast from "react-hot-toast";
import { useNavigate } from "react-router-dom";
import { usePermissions } from "@/api/usePermissions.js";
import { useTheme } from "@/context/ThemeContext.jsx";

import { BanknotesIcon, ArrowUturnLeftIcon } from "@heroicons/react/24/solid";

export default function BanksCreate() {
  const navigate = useNavigate();
  const { loading: permsLoading, canFor } = usePermissions();
  const can = useMemo(
    () =>
      (typeof canFor === "function"
        ? canFor("bank")
        : { view: false, create: false, update: false, delete: false }),
    [canFor]
  );

  const { theme } = useTheme();

  const [form, setForm] = useState({
    bank_name: "",
    account_number: "",
    balance: 0,
    image: null,
  });

  const [submitting, setSubmitting] = useState(false);

  const setField = (k, v) => setForm((prev) => ({ ...prev, [k]: v }));

  const submit = async (e) => {
    e.preventDefault();
    if (!can?.create) return toast.error("No permission");

    try {
      setSubmitting(true);
      const fd = new FormData();
      fd.append("bank_name", form.bank_name);
      fd.append("account_number", form.account_number);
      fd.append("balance", String(form.balance ?? 0));
      if (form.image) fd.append("image", form.image);

      const res = await axios.post("/api/banks", fd, {
        headers: { "Content-Type": "multipart/form-data" },
      });

      toast.success("Bank created");
      navigate("/banks");
    } catch (e2) {
      toast.error(e2?.response?.data?.message || "Create failed");
    } finally {
      setSubmitting(false);
    }
  };

  if (permsLoading) return <div className="p-6">Loading…</div>;
  if (!can?.create) return <div className="p-6 text-sm text-gray-700">You don't have permission to create banks.</div>;

  return (
    <div className="p-4">
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 shadow-sm">
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100 dark:border-slate-700">
          <div className="flex items-center gap-3">
            <div className="p-2 rounded-lg shadow-sm" style={{ background: "linear-gradient(to bottom right, #3b82f6, #2563eb)" }}>
              <BanknotesIcon className="w-5 h-5 text-white" />
            </div>
            <div>
              <h1 className="text-lg font-semibold text-gray-900 dark:text-white">Create Bank</h1>
              <p className="text-xs text-gray-500 dark:text-gray-400">Add new bank account</p>
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
            <span className="text-sm font-medium text-gray-700 dark:text-gray-200">Starting balance</span>
            <input
              type="number"
              value={form.balance}
              onChange={(e) => setField("balance", e.target.value)}
              className="mt-1 w-full h-10 px-3 rounded border border-gray-300 dark:border-slate-600 bg-white dark:bg-slate-900 text-sm"
            />
          </label>

          <label className="block">
            <span className="text-sm font-medium text-gray-700 dark:text-gray-200">Bank image (optional)</span>
            <div className="mt-2">
              <FilePond
                files={form.image ? [{ source: form.image, options: { type: "local" } }] : []}
                allowMultiple={false}
                maxFiles={1}
                acceptedFileTypes={["image/*"]}
                onupdatefiles={(fileItems) => {
                  const fileItem = fileItems[0];
                  const file = fileItem?.file;
                  setField("image", file || null);
                }}
                labelIdle="Drop image or browse"
              />
            </div>
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
              disabled={submitting}
              className="rounded-md px-4 py-2 text-sm font-semibold bg-blue-600 text-white disabled:opacity-60"
            >
              {submitting ? "Creating…" : "Create"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

