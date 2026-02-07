import React, { useState, useEffect, useCallback } from "react";
import { Navigate } from "react-router-dom";
import { useLicense } from "../context/LicenseContext.jsx";
import axios from "axios";

export default function ProtectedRoute({ children }) {
  const token = localStorage.getItem("token");
  const { loading: licenseLoading, valid: licenseValid } = useLicense();
  
  // Local state to track license status independently
  const [isLicenseValid, setIsLicenseValid] = useState(null);
  const [checkingLicense, setCheckingLicense] = useState(true);

  // Check license status explicitly
  const checkLicense = useCallback(async () => {
    if (!token) {
      setCheckingLicense(false);
      return;
    }

    try {
      const response = await axios.get("/api/license/status", {
        headers: { Authorization: `Bearer ${token}` }
      });
      setIsLicenseValid(response.data.valid === true);
    } catch (error) {
      // On API error, be permissive - let the user in
      // Server-side middleware will handle enforcement
      console.warn("License check failed:", error);
      setIsLicenseValid(true); // Allow access, server will enforce
    } finally {
      setCheckingLicense(false);
    }
  }, [token]);

  useEffect(() => {
    // Only check license if LicenseContext is done loading
    if (!licenseLoading) {
      checkLicense();
    }
  }, [licenseLoading, checkLicense]);

  // Check authentication first
  if (!token) {
    return <Navigate to="/" replace />;
  }

  // While checking license status, show children
  if (checkingLicense || licenseLoading) {
    return children;
  }

  // If we have an explicit license check result and it's invalid, redirect
  if (isLicenseValid === false && !licenseValid) {
    return <Navigate to="/activate" replace />;
  }

  return children;
}
