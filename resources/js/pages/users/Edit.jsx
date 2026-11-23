import { useEffect, useState } from "react";
import toast from "react-hot-toast";
import { getUser, updateUser } from "@/api/users";
import { useNavigate, useParams } from "react-router-dom";
import { usePermissions } from "@/api/usePermissions.js";
import { extractApiErrorMessage } from "@/utils/operatorPinHelpers.js";
import UserForm from "./UserForm.jsx";

export default function EditUser() {
  const { id } = useParams();
  const nav = useNavigate();
  const [initial, setInitial] = useState(null);
  const [loadError, setLoadError] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const { loading: permsLoading, canFor } = usePermissions();
  const can = canFor("user");

  useEffect(() => {
    if (permsLoading || !can.update) return;
    let active = true;
    (async () => {
      try {
        setLoadError("");
        const { data } = await getUser(id);
        if (active) setInitial(data);
      } catch (err) {
        const msg = extractApiErrorMessage(err, "Failed to load user.");
        toast.error(msg);
        if (active) setLoadError(msg);
      }
    })();
    return () => {
      active = false;
    };
  }, [id, permsLoading, can.update]);

  const onSubmit = async (payload) => {
    setSubmitting(true);
    try {
      await updateUser(id, payload);
      nav("/users");
    } catch (err) {
      toast.error(extractApiErrorMessage(err, "Failed to update user."));
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

  if (loadError) {
    return (
      <div className="p-6 text-sm text-rose-600">
        {loadError}
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
