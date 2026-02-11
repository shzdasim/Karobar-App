
import React, { useState, useEffect, useMemo } from "react";
import axios from "axios";
import { useTheme } from "@/context/ThemeContext";

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
  
  const { theme } = useTheme();
  const token = localStorage.getItem("token");

  // Memoize theme colors for performance
  const themeColors = useMemo(() => {
    if (!theme) {
      return {
        primary: '#3b82f6',
        primaryHover: '#2563eb',
        primaryLight: '#dbeafe',
      };
    }
    return {
      primary: theme.primary_color || '#3b82f6',
      primaryHover: theme.primary_hover || '#2563eb',
      primaryLight: theme.primary_light || '#dbeafe',
    };
  }, [theme]);

  // Calculate text color for primary button
  const primaryTextColor = useMemo(() => 
    getContrastText(themeColors.primaryHover || themeColors.primary), 
    [themeColors.primary, themeColors.primaryHover]
  );

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
        localStorage.setItem("user", JSON.stringify(res.data));
      })
      .catch((err) => {
        setMessage("Failed to update profile.");
        console.error(err);
      })
      .finally(() => setLoading(false));
  };

  return (
    <div className="max-w-xl mx-auto bg-white dark:bg-slate-800 p-6 rounded-lg shadow">
      <h2 className="text-2xl font-bold mb-4 dark:text-gray-100">My Profile</h2>
      {message && <p className="mb-4 text-green-600 dark:text-green-400">{message}</p>}
      <form onSubmit={handleSubmit} className="space-y-4">
        <div>
          <label className="block font-medium dark:text-gray-300">Name</label>
          <input
            type="text"
            name="name"
            value={form.name}
            onChange={handleChange}
            className="w-full border rounded px-3 py-2 focus:outline-none focus:ring-2 dark:bg-slate-700/70 dark:border-slate-600 dark:text-gray-100 dark:placeholder-gray-400"
            style={{ '--tw-ring-color': themeColors.primary }}
          />
        </div>
        <div>
          <label className="block font-medium dark:text-gray-300">Email</label>
          <input
            type="email"
            name="email"
            value={form.email}
            onChange={handleChange}
            className="w-full border rounded px-3 py-2 focus:outline-none focus:ring-2 dark:bg-slate-700/70 dark:border-slate-600 dark:text-gray-100 dark:placeholder-gray-400"
            style={{ '--tw-ring-color': themeColors.primary }}
          />
        </div>
        <div>
          <label className="block font-medium dark:text-gray-300">Password (optional)</label>
          <input
            type="password"
            name="password"
            value={form.password}
            onChange={handleChange}
            className="w-full border rounded px-3 py-2 focus:outline-none focus:ring-2 dark:bg-slate-700/70 dark:border-slate-600 dark:text-gray-100 dark:placeholder-gray-400"
            style={{ '--tw-ring-color': themeColors.primary }}
          />
        </div>
        <div>
          <label className="block font-medium dark:text-gray-300">Confirm Password</label>
          <input
            type="password"
            name="password_confirmation"
            value={form.password_confirmation}
            onChange={handleChange}
            className="w-full border rounded px-3 py-2 focus:outline-none focus:ring-2 dark:bg-slate-700/70 dark:border-slate-600 dark:text-gray-100 dark:placeholder-gray-400"
            style={{ '--tw-ring-color': themeColors.primary }}
          />
        </div>
        <button
          type="submit"
          disabled={loading}
          className="px-6 py-2 rounded font-semibold transition-all duration-200"
          style={{
            background: `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})`,
            color: primaryTextColor,
            boxShadow: `0 4px 14px 0 ${themeColors.primary}40`,
            opacity: loading ? 0.6 : 1,
            cursor: loading ? 'not-allowed' : 'pointer'
          }}
        >
          {loading ? "Updating..." : "Update Profile"}
        </button>
      </form>
    </div>
  );
}

