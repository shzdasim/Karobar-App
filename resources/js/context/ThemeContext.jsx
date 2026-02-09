import { createContext, useContext, useState, useEffect, useCallback } from 'react';
import axios from 'axios';

const ThemeContext = createContext(null);

const DEFAULT_THEME = {
  primary_color: '#3b82f6',
  primary_hover: '#2563eb',
  primary_light: '#dbeafe',
  secondary_color: '#8b5cf6',
  secondary_hover: '#7c3aed',
  secondary_light: '#ede9fe',
  tertiary_color: '#06b6d4',
  tertiary_hover: '#0891b2',
  tertiary_light: '#cffafe',
  background_color: '#f8fafc',
  surface_color: '#ffffff',
  text_primary: '#1e293b',
  text_secondary: '#64748b',
  success_color: '#10b981',
  warning_color: '#f59e0b',
  danger_color: '#ef4444',
  border_color: '#e2e8f0',
  shadow_color: '#1e293b',
};

export function ThemeProvider({ children }) {
  const [theme, setTheme] = useState(DEFAULT_THEME);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  // Fetch active theme from server
  const fetchTheme = useCallback(async () => {
    try {
      setLoading(true);
      const { data } = await axios.get('/api/theme-settings/active');
      
      if (data) {
        setTheme({
          primary_color: data.primary_color || DEFAULT_THEME.primary_color,
          primary_hover: data.primary_hover || DEFAULT_THEME.primary_hover,
          primary_light: data.primary_light || DEFAULT_THEME.primary_light,
          secondary_color: data.secondary_color || DEFAULT_THEME.secondary_color,
          secondary_hover: data.secondary_hover || DEFAULT_THEME.secondary_hover,
          secondary_light: data.secondary_light || DEFAULT_THEME.secondary_light,
          tertiary_color: data.tertiary_color || DEFAULT_THEME.tertiary_color,
          tertiary_hover: data.tertiary_hover || DEFAULT_THEME.tertiary_hover,
          tertiary_light: data.tertiary_light || DEFAULT_THEME.tertiary_light,
          background_color: data.background_color || DEFAULT_THEME.background_color,
          surface_color: data.surface_color || DEFAULT_THEME.surface_color,
          text_primary: data.text_primary || DEFAULT_THEME.text_primary,
          text_secondary: data.text_secondary || DEFAULT_THEME.text_secondary,
          success_color: data.success_color || DEFAULT_THEME.success_color,
          warning_color: data.warning_color || DEFAULT_THEME.warning_color,
          danger_color: data.danger_color || DEFAULT_THEME.danger_color,
          border_color: data.border_color || DEFAULT_THEME.border_color,
          shadow_color: data.shadow_color || DEFAULT_THEME.shadow_color,
        });
      }
    } catch (err) {
      console.error('Failed to fetch theme:', err);
      setError(err);
    } finally {
      setLoading(false);
    }
  }, []);

  // Fetch theme on mount
  useEffect(() => {
    fetchTheme();
    
    // Listen for theme changes from other tabs/windows
    const handleThemeChange = (e) => {
      if (e.detail && e.detail.theme) {
        setTheme(e.detail.theme);
      }
    };
    
    window.addEventListener('themeChanged', handleThemeChange);
    return () => window.removeEventListener('themeChanged', handleThemeChange);
  }, [fetchTheme]);

  // Apply CSS variables to document
  useEffect(() => {
    const root = document.documentElement;
    
    // Primary colors
    root.style.setProperty('--color-primary', theme.primary_color);
    root.style.setProperty('--color-primary-hover', theme.primary_hover);
    root.style.setProperty('--color-primary-light', theme.primary_light);
    
    // Secondary colors
    root.style.setProperty('--color-secondary', theme.secondary_color);
    root.style.setProperty('--color-secondary-hover', theme.secondary_hover);
    root.style.setProperty('--color-secondary-light', theme.secondary_light);
    
    // Tertiary colors
    root.style.setProperty('--color-tertiary', theme.tertiary_color);
    root.style.setProperty('--color-tertiary-hover', theme.tertiary_hover);
    root.style.setProperty('--color-tertiary-light', theme.tertiary_light);
    
    // Background & Surface
    root.style.setProperty('--color-background', theme.background_color);
    root.style.setProperty('--color-surface', theme.surface_color);
    
    // Text colors
    root.style.setProperty('--color-text-primary', theme.text_primary);
    root.style.setProperty('--color-text-secondary', theme.text_secondary);
    
    // Status colors
    root.style.setProperty('--color-success', theme.success_color);
    root.style.setProperty('--color-warning', theme.warning_color);
    root.style.setProperty('--color-danger', theme.danger_color);
    
    // UI elements
    root.style.setProperty('--color-border', theme.border_color);
    root.style.setProperty('--color-shadow', theme.shadow_color);
    
    // Also set Tailwind-compatible CSS variables for dynamic styling
    root.style.setProperty('--tw-color-primary-500', theme.primary_color);
    root.style.setProperty('--tw-color-primary-600', theme.primary_hover);
    root.style.setProperty('--tw-color-primary-100', theme.primary_light);
    
  }, [theme]);

  // Update theme
  const updateTheme = useCallback(async (updates) => {
    try {
      const newTheme = { ...theme, ...updates };
      setTheme(newTheme);
      
      // Apply CSS variables immediately for responsive feel
      const root = document.documentElement;
      Object.entries(updates).forEach(([key, value]) => {
        const cssVar = `--color-${key.replace(/_/g, '-')}`;
        root.style.setProperty(cssVar, value);
      });
      
      return newTheme;
    } catch (err) {
      console.error('Failed to update theme locally:', err);
      throw err;
    }
  }, [theme]);

  // Save theme to server - updates the active theme
  const saveTheme = useCallback(async (themeData) => {
    try {
      // Immediately update local state for instant feedback
      const updatedTheme = {
        ...theme,
        ...themeData,
      };
      setTheme(updatedTheme);
      
      // Apply CSS variables immediately
      const root = document.documentElement;
      Object.entries(themeData).forEach(([key, value]) => {
        const cssVar = `--color-${key.replace(/_/g, '-')}`;
        root.style.setProperty(cssVar, value);
      });
      
      console.log('Saving theme to server:', themeData);
      
      // Save to server and use the response directly (no refetch to avoid race conditions)
      const { data } = await axios.put('/api/theme-settings/active', themeData);
      console.log('Theme saved successfully:', data);
      
      // Update with fresh data from server
      setTheme(data);
      
      // Apply all CSS variables from server response
      const cssVars = {
        primary_color: data.primary_color,
        primary_hover: data.primary_hover,
        primary_light: data.primary_light,
        secondary_color: data.secondary_color,
        secondary_hover: data.secondary_hover,
        secondary_light: data.secondary_light,
        tertiary_color: data.tertiary_color,
        tertiary_hover: data.tertiary_hover,
        tertiary_light: data.tertiary_light,
        background_color: data.background_color,
        surface_color: data.surface_color,
        text_primary: data.text_primary,
        text_secondary: data.text_secondary,
        success_color: data.success_color,
        warning_color: data.warning_color,
        danger_color: data.danger_color,
        border_color: data.border_color,
        shadow_color: data.shadow_color,
      };
      Object.entries(cssVars).forEach(([key, value]) => {
        if (value) {
          const cssVar = `--color-${key.replace(/_/g, '-')}`;
          root.style.setProperty(cssVar, value);
        }
      });
      
      // Dispatch event for other components
      window.dispatchEvent(new CustomEvent('themeChanged', { detail: { theme: data } }));
      
      return data;
    } catch (err) {
      console.error('Failed to save theme:', err.response || err);
      // Revert to saved theme on error - fetch fresh data
      fetchTheme();
      throw err;
    }
  }, [fetchTheme]);

  // Activate a specific theme
  const activateTheme = useCallback(async (id) => {
    try {
      const { data } = await axios.put(`/api/theme-settings/${id}/activate`);
      
      // Immediately update local state for instant feedback
      setTheme(data);
      
      // Apply CSS variables immediately
      const root = document.documentElement;
      root.style.setProperty('--color-primary', data.primary_color);
      root.style.setProperty('--color-primary-hover', data.primary_hover);
      root.style.setProperty('--color-primary-light', data.primary_light);
      root.style.setProperty('--color-secondary', data.secondary_color);
      root.style.setProperty('--color-secondary-hover', data.secondary_hover);
      root.style.setProperty('--color-secondary-light', data.secondary_light);
      root.style.setProperty('--color-tertiary', data.tertiary_color);
      root.style.setProperty('--color-tertiary-hover', data.tertiary_hover);
      root.style.setProperty('--color-tertiary-light', data.tertiary_light);
      
      // Dispatch event for other components
      window.dispatchEvent(new CustomEvent('themeChanged', { detail: { theme: data } }));
      
      return data;
    } catch (err) {
      console.error('Failed to activate theme:', err);
      throw err;
    }
  }, []);

  const value = {
    theme,
    loading,
    error,
    fetchTheme,
    updateTheme,
    saveTheme,
    activateTheme,
    isDark: (theme.background_color || '').startsWith('#1') || (theme.background_color || '').startsWith('#0'),
  };

  return (
    <ThemeContext.Provider value={value}>
      {children}
    </ThemeContext.Provider>
  );
}

export function useTheme() {
  const context = useContext(ThemeContext);
  if (!context) {
    throw new Error('useTheme must be used within a ThemeProvider');
  }
  return context;
}

// Helper hook for primary color variants
export function usePrimaryColor() {
  const { theme } = useTheme();
  return {
    DEFAULT: theme.primary_color,
    hover: theme.primary_hover,
    light: theme.primary_light,
  };
}

// Helper hook for secondary color variants
export function useSecondaryColor() {
  const { theme } = useTheme();
  return {
    DEFAULT: theme.secondary_color,
    hover: theme.secondary_hover,
    light: theme.secondary_light,
  };
}

// Helper hook for tertiary color variants
export function useTertiaryColor() {
  const { theme } = useTheme();
  return {
    DEFAULT: theme.tertiary_color,
    hover: theme.tertiary_hover,
    light: theme.tertiary_light,
  };
}

