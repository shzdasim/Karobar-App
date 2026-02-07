import React from "react";
import { Navigate } from "react-router-dom";

/**
 * AuthGuard - Redirects users based on authentication status
 * 
 * If authenticated and trying to access login page -> redirect to dashboard
 * If not authenticated and trying to access protected route -> redirect to login
 */
export default function AuthGuard({ children, requireAuth = true }) {
  const token = localStorage.getItem("token");
  const isAuthenticated = !!token;

  if (requireAuth) {
    // Protected route - require authentication
    if (!isAuthenticated) {
      return <Navigate to="/" replace />;
    }
    return children;
  }

  // Public route - redirect to dashboard if already authenticated
  if (isAuthenticated) {
    return <Navigate to="/dashboard" replace />;
  }

  return children;
}

