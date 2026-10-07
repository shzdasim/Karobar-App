import { useEffect, useState } from "react";
import { getUser, updateUser } from "@/api/users";
import { useNavigate, useParams } from "react-router-dom";
import UserForm from "./UserForm.jsx";

export default function EditUser() {
  const { id } = useParams();
  const nav = useNavigate();
  const [initial, setInitial] = useState(null);
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    (async () => {
      const { data } = await getUser(id);
      setInitial(data);
    })();
  }, [id]);

  const onSubmit = async (payload) => {
    setSubmitting(true);
    try {
      await updateUser(id, payload);
      nav("/users");
    } finally {
      setSubmitting(false);
    }
  };

  if (!initial) {
    return (
      <div className="people-form-page">
        <p className="people-hint">Loading user…</p>
      </div>
    );
  }

  return <UserForm onSubmit={onSubmit} initial={initial} submitting={submitting} />;
}
