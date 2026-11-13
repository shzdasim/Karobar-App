import { createContext, useContext, useState, useEffect, useCallback, useRef, useMemo, lazy, Suspense } from 'react';
import { flushSync } from 'react-dom';
import axios from 'axios';
import { getButtonTextColor } from '@/utils/buttonStyles';

const ThemeCurtainStage = lazy(() =>
  import('@/mascot').then((m) => ({ default: m.ThemeCurtainStage }))
);

const ThemeContext = createContext(null);

// ============================================================================
// THEME A: APPLE MACOS THEME (Liquid Glass, 24px blur, 10px squircle)
// ============================================================================
export const APPLE_LIGHT_THEME = {
  name: 'Apple Light',
  primary_color: '#007aff',
  primary_hover: '#0062cc',
  primary_light: '#e5f1ff',
  secondary_color: '#5856d6',
  secondary_hover: '#4745b4',
  secondary_light: '#eeedfb',
  tertiary_color: '#34c759',
  tertiary_hover: '#28a745',
  tertiary_light: '#eafaf0',
  background_color: '#f5f5f7',
  surface_color: '#ffffff',
  text_primary: '#1d1d1f',
  text_secondary: '#86868b',
  success_color: '#34c759',
  warning_color: '#ff9500',
  danger_color: '#ff3b30',
  border_color: '#d2d2d7',
  shadow_color: '#86868b',
  button_style: 'apple',
  sidebar_template: 'glass',
  topbar_template: 'glass',
};

export const APPLE_DARK_THEME = {
  name: 'Apple Dark',
  primary_color: '#0a84ff',
  primary_hover: '#409cff',
  primary_light: '#143a66',
  secondary_color: '#5e5ce6',
  secondary_hover: '#7977eb',
  secondary_light: '#2a2866',
  tertiary_color: '#30d158',
  tertiary_hover: '#57dc79',
  tertiary_light: '#164d26',
  background_color: '#000000',
  surface_color: '#1c1c1e',
  text_primary: '#f5f5f7',
  text_secondary: '#86868b',
  success_color: '#30d158',
  warning_color: '#ffd60a',
  danger_color: '#ff453a',
  border_color: '#38383a',
  shadow_color: '#000000',
  button_style: 'apple',
  sidebar_template: 'glass',
  topbar_template: 'glass',
};

// ============================================================================
// THEME B: WINDOWS 11 FLUENT THEME (WinUI 3, Mica, 4px-8px, 3D bottom bevel)
// ============================================================================
export const WINDOWS_LIGHT_THEME = {
  name: 'Windows Light',
  primary_color: '#005fb8',
  primary_hover: '#0054a6',
  primary_light: '#e5f1fb',
  secondary_color: '#008272',
  secondary_hover: '#007365',
  secondary_light: '#e0f3f0',
  tertiary_color: '#c43e1c',
  tertiary_hover: '#af3618',
  tertiary_light: '#fbe9e5',
  background_color: '#F3F3F3',
  surface_color: '#FFFFFF',
  text_primary: '#1B1B1B',
  text_secondary: '#5D5D5D',
  success_color: '#0f7b0f',
  warning_color: '#9d5d00',
  danger_color: '#c42b1c',
  border_color: '#E5E5E5',
  shadow_color: '#D1D1D1',
  button_style: 'windows',
  sidebar_template: 'classic',
  topbar_template: 'classic',
};

export const WINDOWS_DARK_THEME = {
  name: 'Windows Dark',
  primary_color: '#60cdff',
  primary_hover: '#70d4ff',
  primary_light: '#10394d',
  secondary_color: '#4cc2ba',
  secondary_hover: '#5dd0c8',
  secondary_light: '#123d3a',
  tertiary_color: '#ff8c68',
  tertiary_hover: '#ff9a7b',
  tertiary_light: '#4d261a',
  background_color: '#202020',
  surface_color: '#2C2C2C',
  text_primary: '#E0E0E0',
  text_secondary: '#9E9E9E',
  success_color: '#6ccb5f',
  warning_color: '#fce100',
  danger_color: '#ff99a4',
  border_color: '#383838',
  shadow_color: '#141414',
  button_style: 'windows',
  sidebar_template: 'classic',
  topbar_template: 'classic',
};

// Dark mode theme presets
export const DARK_THEME = WINDOWS_DARK_THEME;
export const LIGHT_THEME = WINDOWS_LIGHT_THEME;
const DEFAULT_THEME = LIGHT_THEME;
const CURTAIN_DURATION_MS = 2800;

export function detectThemeType(theme) {
  if (!theme) return 'windows';
  const name = String(theme.name || '').toLowerCase();
  const btn = String(theme.button_style || '').toLowerCase();
  const bg = String(theme.background_color || '').toLowerCase();

  if (name.includes('apple') || name.includes('cupertino') || btn === 'apple' || bg === '#f5f5f7' || bg === '#000000') {
    return 'apple';
  }
  if (name.includes('windows') || name.includes('fluent') || btn === 'windows' || bg === '#f3f3f3' || bg === '#202020') {
    return 'windows';
  }
  return 'default';
}

function applyThemeCssVariables(targetTheme, darkFlag) {
  const root = document.documentElement;
  if (typeof darkFlag === 'boolean') {
    root.classList.toggle('dark', darkFlag);
  }
  const themeType = detectThemeType(targetTheme);
  root.setAttribute('data-theme', themeType);
  root.setAttribute('data-btn-style', targetTheme.button_style || 'windows');
  root.style.setProperty(
    '--color-button-text',
    getButtonTextColor(targetTheme.primary_color || '#005fb8')
  );
  root.style.setProperty('--color-primary', targetTheme.primary_color);
  root.style.setProperty('--color-primary-hover', targetTheme.primary_hover);
  root.style.setProperty('--color-primary-light', targetTheme.primary_light);
  root.style.setProperty('--color-secondary', targetTheme.secondary_color);
  root.style.setProperty('--color-secondary-hover', targetTheme.secondary_hover);
  root.style.setProperty('--color-secondary-light', targetTheme.secondary_light);
  root.style.setProperty('--color-tertiary', targetTheme.tertiary_color);
  root.style.setProperty('--color-tertiary-hover', targetTheme.tertiary_hover);
  root.style.setProperty('--color-tertiary-light', targetTheme.tertiary_light);
  root.style.setProperty('--color-background', targetTheme.background_color);
  root.style.setProperty('--color-surface', targetTheme.surface_color);
  root.style.setProperty('--color-text-primary', targetTheme.text_primary);
  root.style.setProperty('--color-text-secondary', targetTheme.text_secondary);
  root.style.setProperty('--color-success', targetTheme.success_color);
  root.style.setProperty('--color-warning', targetTheme.warning_color);
  root.style.setProperty('--color-danger', targetTheme.danger_color);
  root.style.setProperty('--color-border', targetTheme.border_color);
  root.style.setProperty('--color-shadow', targetTheme.shadow_color);
}

const THEME_CACHE_KEY = 'karobar_active_theme_v1';

export function ThemeProvider({ children }) {
  const [theme, setTheme] = useState(() => {
    if (typeof window !== 'undefined') {
      try {
        const cached = localStorage.getItem(THEME_CACHE_KEY);
        if (cached) {
          const parsed = JSON.parse(cached);
          const isDarkSaved = localStorage.getItem('theme_mode') === 'dark';
          const themeType = detectThemeType(parsed);
          let base = isDarkSaved ? DARK_THEME : DEFAULT_THEME;
          if (themeType === 'apple') {
            base = isDarkSaved ? APPLE_DARK_THEME : APPLE_LIGHT_THEME;
          } else if (themeType === 'windows') {
            base = isDarkSaved ? WINDOWS_DARK_THEME : WINDOWS_LIGHT_THEME;
          }
          return { ...base, ...parsed };
        }
      } catch {
        /* ignore */
      }
      if (localStorage.getItem('theme_mode') === 'dark') {
        return DARK_THEME;
      }
    }
    return DEFAULT_THEME;
  });
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);
  const [isDark, setIsDark] = useState(() => {
    if (typeof window !== 'undefined') {
      return localStorage.getItem('theme_mode') === 'dark';
    }
    return false;
  });
  const [curtainTransition, setCurtainTransition] = useState(null);
  const transitionTimersRef = useRef([]);

  const clearTransitionTimers = useCallback(() => {
    transitionTimersRef.current.forEach((id) => clearTimeout(id));
    transitionTimersRef.current = [];
  }, []);

  useEffect(() => {
    return () => clearTransitionTimers();
  }, [clearTransitionTimers]);

  // Check if current theme is dark
  const checkIsDark = useCallback((currentTheme) => {
    const bg = currentTheme.background_color || '';
    // Check for dark background colors (starts with #1, #0, or dark slates)
    return bg.startsWith('#1') || bg.startsWith('#0') || bg.startsWith('#2') ||
           bg.includes('slate-800') || bg.includes('slate-900') ||
           bg.includes('gray-800') || bg.includes('gray-900');
  }, []);

  // Toggle between light and dark mode with the Mascot & Curtain Cord choreography
  const toggleTheme = useCallback((triggerElement = null) => {
    if (curtainTransition?.active) {
      return theme;
    }

    clearTransitionTimers();

    const newIsDark = !isDark;
    const direction = newIsDark ? 'to-dark' : 'to-light';
    const currentType = detectThemeType(theme);
    let basePreset = newIsDark ? DARK_THEME : LIGHT_THEME;
    if (currentType === 'apple') {
      basePreset = newIsDark ? APPLE_DARK_THEME : APPLE_LIGHT_THEME;
    } else if (currentType === 'windows') {
      basePreset = newIsDark ? WINDOWS_DARK_THEME : WINDOWS_LIGHT_THEME;
    }
    const newTheme = {
      ...basePreset,
      name: theme.name || basePreset.name,
      primary_color: theme.primary_color || basePreset.primary_color,
      primary_hover: theme.primary_hover || basePreset.primary_hover,
      secondary_color: theme.secondary_color || basePreset.secondary_color,
      secondary_hover: theme.secondary_hover || basePreset.secondary_hover,
      tertiary_color: theme.tertiary_color || basePreset.tertiary_color,
      tertiary_hover: theme.tertiary_hover || basePreset.tertiary_hover,
      button_style: theme.button_style || basePreset.button_style,
      sidebar_template: theme.sidebar_template || basePreset.sidebar_template,
      topbar_template: theme.topbar_template || basePreset.topbar_template,
    };

    const btnEl =
      (triggerElement instanceof Element ? triggerElement : null) ||
      (typeof document !== 'undefined' ? document.querySelector('[data-theme-toggle-btn]') : null);
    const rect = btnEl ? btnEl.getBoundingClientRect() : null;
    const rawX = rect
      ? rect.left + rect.width / 2
      : typeof window !== 'undefined'
        ? window.innerWidth - 260
        : 900;
    const cordX =
      typeof window !== 'undefined'
        ? Math.min(Math.max(rawX, 140), window.innerWidth - 120)
        : rawX;

    const commitThemeDomAndState = () => {
      setIsDark(newIsDark);
      setTheme(newTheme);
      applyThemeCssVariables(newTheme, newIsDark);
      localStorage.setItem('theme_mode', newIsDark ? 'dark' : 'light');
      window.dispatchEvent(
        new CustomEvent('themeChanged', { detail: { theme: newTheme, isDark: newIsDark } })
      );
    };

    const transitionId = Date.now();
    const prefersReducedMotion =
      typeof window !== 'undefined' &&
      window.matchMedia?.('(prefers-reduced-motion: reduce)')?.matches;

    if (prefersReducedMotion) {
      commitThemeDomAndState();
      btnEl?.focus?.({ preventScroll: true });
      return newTheme;
    }

    if (typeof document !== 'undefined' && typeof document.startViewTransition === 'function') {
      const root = document.documentElement;
      root.dataset.curtainTransition = direction;

      const updateCallback = () => {
        flushSync(() => {
          commitThemeDomAndState();
          setCurtainTransition({
            active: true,
            direction,
            cordX,
            fallback: false,
            id: transitionId,
          });
        });
      };

      let vt;
      try {
        vt = document.startViewTransition({
          update: updateCallback,
          types: [direction],
        });
      } catch {
        vt = document.startViewTransition(updateCallback);
      }

      const cleanup = () => {
        delete root.dataset.curtainTransition;
        setCurtainTransition((prev) => (prev?.id === transitionId ? null : prev));
        // Route focus back to the toggle control after view transition finishes
        btnEl?.focus?.({ preventScroll: true });
      };

      if (vt && vt.finished) {
        vt.finished.finally(cleanup);
      }
      const safetyTimer = setTimeout(cleanup, CURTAIN_DURATION_MS + 120);
      transitionTimersRef.current.push(safetyTimer);
    } else {
      // Progressive enhancement fallback when View Transitions API is unavailable
      setCurtainTransition({
        active: true,
        direction,
        cordX,
        fallback: true,
        id: transitionId,
      });

      const midTimer = setTimeout(() => {
        commitThemeDomAndState();
      }, Math.round(CURTAIN_DURATION_MS * 0.52));

      const endTimer = setTimeout(() => {
        setCurtainTransition(null);
        btnEl?.focus?.({ preventScroll: true });
      }, CURTAIN_DURATION_MS);

      transitionTimersRef.current.push(midTimer, endTimer);
    }

    return newTheme;
  }, [isDark, theme, curtainTransition, clearTransitionTimers]);

  // Set dark mode directly
  const setDarkMode = useCallback((dark) => {
    if (isDark !== Boolean(dark)) {
      toggleTheme();
    }
  }, [isDark, toggleTheme]);

  // Load saved theme mode from localStorage on mount
  useEffect(() => {
    const savedMode = localStorage.getItem('theme_mode');
    if (savedMode === 'dark') {
      setIsDark(true);
      setTheme(DARK_THEME);
      applyThemeCssVariables(DARK_THEME, true);
    } else {
      applyThemeCssVariables(LIGHT_THEME, false);
    }
  }, []);

  // Sync dark class with Tailwind when isDark changes
  useEffect(() => {
    if (isDark) {
      document.documentElement.classList.add('dark');
    } else {
      document.documentElement.classList.remove('dark');
    }
  }, [isDark]);

  // Fetch active theme from server
  const fetchTheme = useCallback(async () => {
    try {
      const { data } = await axios.get('/api/theme-settings/active');
      
      if (data) {
        const savedDark = localStorage.getItem('theme_mode') === 'dark';
        const currentType = detectThemeType(data);
        let modeBase = savedDark ? DARK_THEME : DEFAULT_THEME;
        if (currentType === 'apple') {
          modeBase = savedDark ? APPLE_DARK_THEME : APPLE_LIGHT_THEME;
        } else if (currentType === 'windows') {
          modeBase = savedDark ? WINDOWS_DARK_THEME : WINDOWS_LIGHT_THEME;
        }
        const resolvedTheme = {
          name: data.name || modeBase.name,
          primary_color: data.primary_color || modeBase.primary_color,
          primary_hover: data.primary_hover || modeBase.primary_hover,
          primary_light: savedDark ? modeBase.primary_light : (data.primary_light || modeBase.primary_light),
          secondary_color: data.secondary_color || modeBase.secondary_color,
          secondary_hover: data.secondary_hover || modeBase.secondary_hover,
          secondary_light: savedDark ? modeBase.secondary_light : (data.secondary_light || modeBase.secondary_light),
          tertiary_color: data.tertiary_color || modeBase.tertiary_color,
          tertiary_hover: data.tertiary_hover || modeBase.tertiary_hover,
          tertiary_light: savedDark ? modeBase.tertiary_light : (data.tertiary_light || modeBase.tertiary_light),
          background_color: savedDark ? modeBase.background_color : (data.background_color || modeBase.background_color),
          surface_color: savedDark ? modeBase.surface_color : (data.surface_color || modeBase.surface_color),
          text_primary: savedDark ? modeBase.text_primary : (data.text_primary || modeBase.text_primary),
          text_secondary: savedDark ? modeBase.text_secondary : (data.text_secondary || modeBase.text_secondary),
          success_color: data.success_color || modeBase.success_color,
          warning_color: data.warning_color || modeBase.warning_color,
          danger_color: data.danger_color || modeBase.danger_color,
          border_color: savedDark ? modeBase.border_color : (data.border_color || modeBase.border_color),
          shadow_color: savedDark ? modeBase.shadow_color : (data.shadow_color || modeBase.shadow_color),
          button_style: data.button_style || modeBase.button_style,
          sidebar_template: data.sidebar_template || modeBase.sidebar_template,
          topbar_template: data.topbar_template || modeBase.topbar_template,
        };
        setTheme(resolvedTheme);
        applyThemeCssVariables(resolvedTheme, savedDark);
        try {
          localStorage.setItem(THEME_CACHE_KEY, JSON.stringify(resolvedTheme));
        } catch {
          /* ignore */
        }
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
    const themeType = detectThemeType(theme);
    root.setAttribute('data-theme', themeType);

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
    
    // Button style radius
    const radiusMap = {
      'apple':      '0.625rem', // 10px Apple squircle
      'windows':    '0.25rem',  // 4px Windows 11 Fluent
      'tactile':    '0.875rem', // 14px
      'modern':     '0.75rem',  // 12px – modern default
      'rounded':    '0.5rem',   // 8px
      'rounded-sm': '0.375rem', // 6px
      'outlined':   '5px',      // Stacked Lift (stored id "outlined")
      'soft':       '1rem',     // 16px
      'pill':       '5px',      // Letter Cascade (stored id "pill")
    };
    root.style.setProperty('--btn-radius', radiusMap[theme.button_style] || '0.25rem');
    
    // Sidebar template
    root.style.setProperty('--sidebar-template', theme.sidebar_template || 'classic');
    
    // Topbar template
    root.style.setProperty('--topbar-template', theme.topbar_template || 'classic');
    
    // Also set Tailwind-compatible CSS variables for dynamic styling
    root.style.setProperty('--tw-color-primary-500', theme.primary_color);
    root.style.setProperty('--tw-color-primary-600', theme.primary_hover);
    root.style.setProperty('--tw-color-primary-100', theme.primary_light);
    
  }, [theme]);

  // Button style: <html data-btn-style> + --color-button-text drive the
  // app-wide THEME BUTTON SYSTEM in app.css, so every button in the app follows
  // the style picked in Settings → Theme.
  useEffect(() => {
    const root = document.documentElement;
    const themeType = detectThemeType(theme);
    root.setAttribute('data-theme', themeType);
    root.setAttribute('data-btn-style', theme.button_style || 'windows');
    root.style.setProperty(
      '--color-button-text',
      getButtonTextColor(theme.primary_color || '#005fb8')
    );
  }, [theme.button_style, theme.primary_color, theme.name, theme.background_color]);

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
        button_style: data.button_style,
        sidebar_template: data.sidebar_template,
        topbar_template: data.topbar_template,
      };
      Object.entries(cssVars).forEach(([key, value]) => {
        if (value) {
          const cssVar = `--color-${key.replace(/_/g, '-')}`;
          root.style.setProperty(cssVar, value);
        }
      });
      
      // Apply button style radius
      const radiusMap = {
        'apple':      '0.625rem', // 10px Apple squircle
        'windows':    '0.25rem',  // 4px Windows Fluent
        'modern':     '0.75rem',  // 12px – modern default
        'rounded':    '0.5rem',   // 8px
        'rounded-sm': '0.375rem', // 6px
        'outlined':   '0.5rem',   // 8px
        'soft':       '1rem',     // 16px
        'pill':       '9999px',   // full pill
      };
      root.style.setProperty('--btn-radius', radiusMap[data.button_style] || '0.75rem');
      
      // Apply sidebar template
      root.style.setProperty('--sidebar-template', data.sidebar_template || 'classic');
      
      // Apply topbar template
      root.style.setProperty('--topbar-template', data.topbar_template || 'classic');
      
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
      root.style.setProperty('--sidebar-template', data.sidebar_template || 'classic');
      root.style.setProperty('--topbar-template', data.topbar_template || 'classic');
      
      // Apply button style radius
      const activateRadiusMap = {
        'apple':      '0.625rem',
        'windows':    '0.25rem',
        'modern':     '0.75rem',
        'rounded':    '0.5rem',
        'rounded-sm': '0.375rem',
        'outlined':   '0.5rem',
        'soft':       '1rem',
        'pill':       '9999px',
      };
      root.style.setProperty('--btn-radius', activateRadiusMap[data.button_style] || '0.75rem');
      
      // Dispatch event for other components
      window.dispatchEvent(new CustomEvent('themeChanged', { detail: { theme: data } }));
      
      return data;
    } catch (err) {
      console.error('Failed to activate theme:', err);
      throw err;
    }
  }, []);

  const isTransitioning = Boolean(curtainTransition?.active);

  const value = useMemo(
    () => ({
      theme,
      loading,
      error,
      fetchTheme,
      updateTheme,
      saveTheme,
      activateTheme,
      toggleTheme,
      setDarkMode,
      isDark,
      isTransitioning,
    }),
    [
      theme,
      loading,
      error,
      fetchTheme,
      updateTheme,
      saveTheme,
      activateTheme,
      toggleTheme,
      setDarkMode,
      isDark,
      isTransitioning,
    ]
  );

  return (
    <ThemeContext.Provider value={value}>
      {children}
      {curtainTransition ? (
        <Suspense fallback={null}>
          <ThemeCurtainStage transition={curtainTransition} />
        </Suspense>
      ) : null}
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
