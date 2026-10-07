import { useState } from "react";
import { createUser } from "@/api/users";
import { useNavigate } from "react-router-dom";
import UserForm from "./UserForm.jsx";

export default function CreateUser() {
  const nav = useNavigate();
  const [submitting, setSubmitting] = useState(false);
  const onSubmit = async (payload) => {
    setSubmitting(true);
    try {
      await createUser(payload);
      nav("/users");
    } finally {
      setSubmitting(false);
    }
  };

  return <UserForm onSubmit={onSubmit} submitting={submitting} />;
}
