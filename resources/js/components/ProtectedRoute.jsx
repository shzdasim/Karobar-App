import React from "react";
import { Navigate } from "react-router-dom";
import { useLicense } from "../context/LicenseContext.jsx";

export default function ProtectedRoute({ children }) {
  const token = localStorage.getItem("token");

  // Check authentication first
  if (!token) {
    return <Navigate to="/" replace />;
  }

  // Check license validity
  const { loading, valid } = useLicense();

  // While checking license status, show children (they might be in loading state too)
  // Only redirect to /activate if we've confirmed the license is invalid
  if (loading) {
    return children;
  }

  // Redirect to activation page if license is invalid or missing
  if (!valid) {
    return <Navigate to="/activate" replace />;
  }

  return children;
}
