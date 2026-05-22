import React from "react";
import { useNavigate, useParams } from "react-router-dom";
import QuotationForm from "./QuotationForm";

export default function QuotationsEdit() {
  const navigate = useNavigate();
  const { id } = useParams();

  return (
    <QuotationForm
      quotationId={id}
      onSuccess={(res) => {
        if (res?.id) navigate(`/quotations/${res.id}`);
        else navigate(`/quotations`);
      }}
    />
  );
}

