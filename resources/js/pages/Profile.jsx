import React, { useState, useEffect } from "react";
import axios from "axios";
import { useTheme } from "@/context/ThemeContext";
import {
  UserCircleIcon,
  CheckCircleIcon,
  ExclamationTriangleIcon,
} from "@heroicons/react/24/solid";

// Helper to determine text color based on background brightness
const getContrastText = (hexColor) => {
  hexColor = hexColor.replace('#', '');
  const r = parseInt(hexColor.substring(0, 2), 16);
  const g = parseInt(hexColor.substring(2, 4), 16);
  const b = parseInt(hexColor.substring(4, 6), 16);
  const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
  return luminance > 0.5 ? '#1f2937' : '#ffffff';
};

export default function Profile() {
  const [form, setForm] = useState({
    name: "",
    email: "",
    password: "",
    password_confirmation: "",
  });
  const [loading, setLoading] = useState(false);
  const [message, setMessage] = useState("");
  const [messageTone, setMessageTone] = useState("success");

  const { theme } = useTheme();
  const token = localStorage.getItem("token");

  const primaryColor = theme?.primary_color || '#3b82f6';
  const primaryTextColor = getContrastText(primaryColor);

  // Load user data
  useEffect(() => {
    axios
      .get("/api/user", { headers: { Authorization: `Bearer ${token}` } })
      .then((res) => {
        setForm((prev) => ({
          ...prev,
          name: res.data.name,
          email: res.data.email,
        }));
      })
      .catch((err) => {
        console.error(err);
      });
  }, []);

  const handleChange = (e) => {
    setForm({ ...form, [e.target.name]: e.target.value });
  };

  const handleSubmit = (e) => {
    e.preventDefault();
    setLoading(true);
    setMessage("");

    axios
      .put("/api/profile", form, {
        headers: { Authorization: `Bearer ${token}` },
      })
      .then((res) => {
        setMessage("Profile updated successfully!");
        setMessageTone("success");
        localStorage.setItem("user", JSON.stringify(res.data));
      })
      .catch((err) => {
        setMessage("Failed to update profile.");
        setMessageTone("error");
        console.error(err);
      })
      .finally(() => setLoading(false));
  };

  return (
    <div className="people-form-page people-form-narrow">
      <div className="products-panel">
        {/* Header */}
        <div className="people-form-heading">
          <div className="flex items-center gap-3 min-w-0">
            <div className="products-identity-icon">
              <UserCircleIcon className="w-6 h-6" />
            </div>
            <div className="min-w-0">
              <h1 className="products-title">My Profile</h1>
              <p className="products-subtitle">
                Update your sign-in name, email, and password.
              </p>
            </div>
          </div>
        </div>

        <form onSubmit={handleSubmit} className="people-form">
          {message && (
            <p
              className={`people-message ${
                messageTone === "success" ? "people-message-success" : "people-message-error"
              }`}
              role="status"
            >
              {messageTone === "success" ? (
                <CheckCircleIcon className="w-5 h-5 shrink-0" aria-hidden="true" />
              ) : (
                <ExclamationTriangleIcon className="w-5 h-5 shrink-0" aria-hidden="true" />
              )}
              <span>{message}</span>
            </p>
          )}

          <div className="people-fields">
            <div className="people-field people-field-wide">
              <label className="people-label" htmlFor="profile-name">Name</label>
              <input
                id="profile-name"
                type="text"
                name="name"
                value={form.name}
                onChange={handleChange}
                placeholder="Your full name"
              />
            </div>

            <div className="people-field people-field-wide">
              <label className="people-label" htmlFor="profile-email">Email</label>
              <input
                id="profile-email"
                type="email"
                name="email"
                value={form.email}
                onChange={handleChange}
                placeholder="you@example.com"
              />
            </div>

            <div className="people-field">
              <label className="people-label" htmlFor="profile-password">New password</label>
              <input
                id="profile-password"
                type="password"
                name="password"
                value={form.password}
                onChange={handleChange}
                placeholder="Leave blank to keep current"
              />
            </div>

            <div className="people-field">
              <label className="people-label" htmlFor="profile-password-confirmation">
                Confirm new password
              </label>
              <input
                id="profile-password-confirmation"
                type="password"
                name="password_confirmation"
                value={form.password_confirmation}
                onChange={handleChange}
                placeholder="Repeat the new password"
              />
            </div>
          </div>

          <div className="people-actions">
            <button
              type="submit"
              disabled={loading}
              className="products-action products-action-primary"
              style={{ color: primaryTextColor }}
            >
              {loading ? "Updating…" : "Update Profile"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
