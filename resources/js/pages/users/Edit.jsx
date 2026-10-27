import { useEffect, useState } from "react";
import { getUser, updateUser } from "@/api/users";
import { useNavigate, useParams } from "react-router-dom";
import { usePermissions } from "@/api/usePermissions.js";
import UserForm from "./UserForm.jsx";

export default function EditUser() {
  const { id } = useParams();
  const nav = useNavigate();
  const [initial, setInitial] = useState(null);
  const [submitting, setSubmitting] = useState(false);
  const { loading: permsLoading, canFor } = usePermissions();
  const can = canFor("user");

  useEffect(() => {
    if (permsLoading || !can.update) return;
    (async () => {
      const { data } = await getUser(id);
      setInitial(data);
    })();
  }, [id, permsLoading, can.update]);

  const onSubmit = async (payload) => {
    setSubmitting(true);
    try {
      await updateUser(id, payload);
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

  if (!can.update) {
    return (
      <div className="p-6 text-sm text-rose-600">
        You do not have permission to edit users.
      </div>
    );
  }

  if (!initial) {
    return (
      <div className="people-form-page">
        <p className="people-hint">Loading user…</p>
      </div>
    );
  }

  return <UserForm onSubmit={onSubmit} initial={initial} submitting={submitting} />;
}
