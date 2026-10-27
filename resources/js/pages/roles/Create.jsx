import { useState } from "react";
import { createRole } from "@/api/roles";
import { useNavigate } from "react-router-dom";
import { usePermissions } from "@/api/usePermissions.js";
import RoleForm from "./RoleForm.jsx";

export default function CreateRole() {
  const nav = useNavigate();
  const [submitting, setSubmitting] = useState(false);
  const { loading: permsLoading, canFor } = usePermissions();
  const can = canFor("role");

  const onSubmit = async (payload) => {
    setSubmitting(true);
    try {
      await createRole(payload);
      nav("/roles");
    } finally {
      setSubmitting(false);
    }
  };

  if (permsLoading) return null;

  if (!can.create) {
    return (
      <div className="p-6 text-sm text-rose-600">
        You do not have permission to create roles.
      </div>
    );
  }

  return <RoleForm onSubmit={onSubmit} submitting={submitting} />;
}
