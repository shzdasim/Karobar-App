import React from "react";
import { useNavigate } from "react-router-dom";
import { usePermissions } from "@/api/usePermissions";
import QuotationForm from "./QuotationForm";

export default function QuotationsCreate() {
  const navigate = useNavigate();
  const { canFor } = usePermissions();

  const canCreate = typeof canFor === "function" ? canFor("quotation")?.create : true;

  return (
    <QuotationForm
      quotationId={null}
      onSuccess={(res) => {
        if (res?.id) navigate(`/quotations/${res.id}`);
        else navigate(`/quotations`);
      }}
    />
  );
}

