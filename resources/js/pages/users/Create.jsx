import { useState } from "react";
import { createUser } from "@/api/users";
import { useNavigate } from "react-router-dom";
import { usePermissions } from "@/api/usePermissions.js";
import UserForm from "./UserForm.jsx";

export default function CreateUser() {
  const nav = useNavigate();
  const [submitting, setSubmitting] = useState(false);
  const { loading: permsLoading, canFor } = usePermissions();
  const can = canFor("user");

  const onSubmit = async (payload) => {
    setSubmitting(true);
    try {
      await createUser(payload);
      nav("/users");
    } finally {
      setSubmitting(false);
    }
  };

  if (permsLoading) {
    return (
      <div className="people-form-page">
        <p className="people-hint">Checking permissions…</p>
      </div>
    );
  }

  if (!can.create) {
    return (
      <div className="p-6 text-sm text-rose-600">
        You do not have permission to create users.
      </div>
    );
  }

  return <UserForm onSubmit={onSubmit} submitting={submitting} />;
}
