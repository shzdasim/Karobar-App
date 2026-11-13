// resources/js/components/settings/ThemeSetting.jsx
import { useState, useEffect, useMemo } from "react";
import { Link } from "react-router-dom";
import toast from "react-hot-toast";
import {
  useTheme,
  APPLE_LIGHT_THEME,
  APPLE_DARK_THEME,
  WINDOWS_LIGHT_THEME,
  WINDOWS_DARK_THEME,
  detectThemeType,
} from "@/context/ThemeContext";
import { getThemeButtonClasses, getButtonTextColor } from "@/utils/buttonStyles";
import {
  PaintBrushIcon,
  CheckIcon,
  Cog6ToothIcon,
  Square2StackIcon,
  SparklesIcon,
  DevicePhoneMobileIcon,
  ComputerDesktopIcon,
} from "@heroicons/react/24/solid";
import { ArrowRightIcon } from "@heroicons/react/24/outline";
import {
  GuideBotAvatar,
  useBottomMascotEnabledState,
} from "@/components/GuideBotTrigger.jsx";

// Button style options
const BUTTON_STYLES = [
  {
    id: 'apple',
    name: 'Apple Native',
    icon: '',
    description: 'macOS tactile capsule with specular highlight & micro-press',
    className: 'rounded-[10px]',
    variant: 'apple',
  },
  {
    id: 'windows',
    name: 'Windows Fluent',
    icon: '⊞',
    description: 'WinUI 3 4px key with 3D bottom bevel elevation',
    className: 'rounded',
    variant: 'windows',
  },
  {
    id: 'modern',
    name: 'Modern',
    icon: '✦',
    description: 'Frosted glass with orbiting glow',
    className: 'rounded-xl',
    variant: 'modern'
  },
  {
    id: 'rounded', // stored id kept for saved themes
    name: 'Neumorphic',
    icon: '■',
    description: 'Soft emboss with rising fill',
    className: 'rounded-lg',
    variant: 'neumorphic'
  },
  {
    id: 'soft', // stored id kept for saved themes
    name: 'Slide Reveal',
    icon: '▣',
    description: 'Icon panel sweeps across on hover',
    className: 'rounded-2xl',
    variant: 'slide'
  },
  {
    id: 'pill', // stored id kept for saved themes
    name: 'Letter Cascade',
    icon: '◉',
    description: 'Face drops, letters cascade in',
    className: 'rounded-[5px]',
    variant: 'cascade'
  },
  {
    id: 'outlined', // stored id kept for saved themes
    name: 'Stacked Lift',
    icon: '▢',
    description: 'Lifts with a layered colour trail',
    className: 'rounded-[5px]',
    variant: 'lift'
  },
];

// Color picker component
function ColorInput({ label, color, onChange, disabled }) {
  return (
    <div className="settings-field">
      <label className="settings-label">{label}</label>
      <div className="settings-color">
        <input
          type="color"
          value={color}
          onChange={(e) => onChange(e.target.value)}
          disabled={disabled}
          aria-label={`${label} colour`}
        />
        <input
          type="text"
          value={color}
          onChange={(e) => {
            const val = e.target.value;
            if (/^#[0-9A-Fa-f]{6}$/.test(val)) {
              onChange(val);
            }
          }}
          disabled={disabled}
          aria-label={`${label} colour value`}
          className="g-input flex-1"
        />
      </div>
    </div>
  );
}

// Color palette preset
const PRESETS = [
  { name: 'Apple Cupertino', primary: '#007aff', secondary: '#5856d6', tertiary: '#34c759', themeType: 'apple', buttonStyle: 'apple' },
  { name: 'Windows 11 Fluent', primary: '#005fb8', secondary: '#008272', tertiary: '#c43e1c', themeType: 'windows', buttonStyle: 'windows' },
  { name: 'Blue', primary: '#3b82f6', secondary: '#8b5cf6', tertiary: '#06b6d4' },
  { name: 'Emerald', primary: '#10b981', secondary: '#8b5cf6', tertiary: '#f59e0b' },
  { name: 'Rose', primary: '#f43f5e', secondary: '#8b5cf6', tertiary: '#06b6d4' },
  { name: 'Orange', primary: '#f97316', secondary: '#8b5cf6', tertiary: '#06b6d4' },
  { name: 'Indigo', primary: '#6366f1', secondary: '#ec4899', tertiary: '#14b8a6' },
  { name: 'Slate', primary: '#64748b', secondary: '#8b5cf6', tertiary: '#06b6d4' },
];

function generateVariants(baseColor) {
  const hex = baseColor.replace('#', '');
  const r = parseInt(hex.substring(0, 2), 16);
  const g = parseInt(hex.substring(2, 4), 16);
  const b = parseInt(hex.substring(4, 6), 16);
  const hoverR = (r * 0.8) | 0;
  const hoverG = (g * 0.8) | 0;
  const hoverB = (b * 0.8) | 0;
  const hoverColor = '#' +
    hoverR.toString(16).padStart(2, '0') +
    hoverG.toString(16).padStart(2, '0') +
    hoverB.toString(16).padStart(2, '0');
  return { hover: hoverColor, light: `rgba(${r}, ${g}, ${b}, 0.1)` };
}

export default function ThemeSetting({ disableInputs }) {
  const { theme, saveTheme, activateTheme, loading: themeLoading, isDark } = useTheme();
  const [bottomMascotEnabled, setBottomMascotEnabled] = useBottomMascotEnabledState();

  const handleToggleBottomMascot = (nextChecked) => {
    setBottomMascotEnabled(nextChecked);
    toast.success(
      nextChecked
        ? "Bottom roaming mascot enabled"
        : "Bottom roaming mascot hidden (Topbar Guide Bot stays active)"
    );
  };

  const [themeSettings, setThemeSettings] = useState({
    name: 'Custom Theme',
    primary_color: '#3b82f6',
    primary_hover: '#2563eb',
    primary_light: 'rgba(59, 130, 246, 0.1)',
    secondary_color: '#8b5cf6',
    secondary_hover: '#7c3aed',
    secondary_light: 'rgba(139, 92, 246, 0.1)',
    tertiary_color: '#06b6d4',
    tertiary_hover: '#0891b2',
    tertiary_light: 'rgba(6, 182, 212, 0.1)',
    button_style: 'modern',
  });
  const [saving, setSaving] = useState(false);
  const [hasChanges, setHasChanges] = useState(false);
  // Track which preset is active (null means custom colors)
  const [activePreset, setActivePreset] = useState(null);

  // Load saved theme from context
  useEffect(() => {
    if (theme && theme.primary_color) {
      setThemeSettings({
        name: theme.name || 'Custom Theme',
        primary_color: theme.primary_color,
        primary_hover: theme.primary_hover,
        primary_light: theme.primary_light,
        secondary_color: theme.secondary_color,
        secondary_hover: theme.secondary_hover,
        secondary_light: theme.secondary_light,
        tertiary_color: theme.tertiary_color,
        tertiary_hover: theme.tertiary_hover,
        tertiary_light: theme.tertiary_light,
        button_style: theme.button_style || 'modern',
      });

      // Check if loaded theme matches a preset
      let matchedPreset = null;
      for (const preset of PRESETS) {
        if (
          preset.primary.toLowerCase() === theme.primary_color.toLowerCase() &&
          preset.secondary.toLowerCase() === theme.secondary_color.toLowerCase() &&
          preset.tertiary.toLowerCase() === theme.tertiary_color.toLowerCase()
        ) {
          matchedPreset = preset.name;
          break;
        }
      }
      setActivePreset(matchedPreset);
      setHasChanges(false);
    }
  }, [theme]);

  // Track changes
  useEffect(() => {
    if (theme && theme.primary_color) {
      const isChanged =
        theme.primary_color !== themeSettings.primary_color ||
        theme.secondary_color !== themeSettings.secondary_color ||
        theme.tertiary_color !== themeSettings.tertiary_color ||
        theme.button_style !== themeSettings.button_style;
      setHasChanges(isChanged);
    }
  }, [themeSettings, theme]);

  const handleColorChange = (key, value) => {
    setThemeSettings(prev => {
      const updated = { ...prev, [key]: value };
      if (['primary_color', 'secondary_color', 'tertiary_color'].includes(key)) {
        const variants = generateVariants(value);
        const baseKey = key.replace('_color', '');
        updated[`${baseKey}_hover`] = variants.hover;
        updated[`${baseKey}_light`] = variants.light;
      }
      return updated;
    });
    // Clear preset selection when user modifies colors manually
    setActivePreset(null);
  };

  const handleButtonStyleChange = async (style) => {
    if (disableInputs) {
      toast.error("You don't have permission to update settings.");
      return;
    }

    // Create updated settings first
    const updatedSettings = { ...themeSettings, button_style: style };

    // Update local state immediately for instant preview
    setThemeSettings(updatedSettings);

    // Save immediately for instant effect
    try {
      setSaving(true);
      await saveTheme(updatedSettings);
      toast.success(`Button style: ${BUTTON_STYLES.find(s => s.id === style)?.name || style}`);
      setHasChanges(false);
    } catch (err) {
      toast.error("Failed to update button style");
    } finally {
      setSaving(false);
    }
  };

  const handleSwitchThemeArchetype = async (type) => {
    if (disableInputs) {
      toast.error("You don't have permission to update settings.");
      return;
    }
    const target = type === 'apple' 
      ? (isDark ? APPLE_DARK_THEME : APPLE_LIGHT_THEME)
      : (isDark ? WINDOWS_DARK_THEME : WINDOWS_LIGHT_THEME);

    setThemeSettings(target);
    setActivePreset(type === 'apple' ? 'Apple Cupertino' : 'Windows 11 Fluent');
    try {
      setSaving(true);
      await saveTheme(target);
      toast.success(type === 'apple' ? 'Switched to Apple macOS Theme' : 'Switched to Windows 11 Fluent Theme');
      setHasChanges(false);
    } catch (err) {
      toast.error('Failed to switch theme archetype');
    } finally {
      setSaving(false);
    }
  };

  const applyPreset = async (preset) => {
    if (preset.themeType === 'apple') {
      return handleSwitchThemeArchetype('apple');
    }
    if (preset.themeType === 'windows') {
      return handleSwitchThemeArchetype('windows');
    }

    const v = generateVariants(preset.primary);
    const sv = generateVariants(preset.secondary);
    const tv = generateVariants(preset.tertiary);
    const newThemeSettings = {
      ...themeSettings,
      name: `${preset.name} Theme`,
      primary_color: preset.primary,
      primary_hover: v.hover,
      primary_light: v.light,
      secondary_color: preset.secondary,
      secondary_hover: sv.hover,
      secondary_light: sv.light,
      tertiary_color: preset.tertiary,
      tertiary_hover: tv.hover,
      tertiary_light: tv.light,
      button_style: preset.buttonStyle || themeSettings.button_style || 'modern',
    };

    // Update local state immediately for instant preview
    setThemeSettings(newThemeSettings);
    setActivePreset(preset.name); // Mark this preset as active

    // Save immediately for instant effect across the app
    try {
      setSaving(true);
      await saveTheme(newThemeSettings);
      toast.success(`Applied ${preset.name} theme!`);
      setHasChanges(false);
    } catch (err) {
      toast.error("Failed to apply theme");
    } finally {
      setSaving(false);
    }
  };

  const handleSaveTheme = async () => {
    try {
      setSaving(true);
      console.log('Saving theme settings:', themeSettings);
      const savedTheme = await saveTheme(themeSettings);
      toast.success("Theme saved!");

      // Sync local state with saved theme from server
      setThemeSettings({
        name: savedTheme.name || 'Custom Theme',
        primary_color: savedTheme.primary_color,
        primary_hover: savedTheme.primary_hover,
        primary_light: savedTheme.primary_light,
        secondary_color: savedTheme.secondary_color,
        secondary_hover: savedTheme.secondary_hover,
        secondary_light: savedTheme.secondary_light,
        tertiary_color: savedTheme.tertiary_color,
        tertiary_hover: savedTheme.tertiary_hover,
        tertiary_light: savedTheme.tertiary_light,
        button_style: savedTheme.button_style || 'modern',
      });
      setHasChanges(false);
    } catch (err) {
      console.error('Save error:', err);
      toast.error("Failed to save theme: " + (err.message || 'Unknown error'));
    } finally {
      setSaving(false);
    }
  };

  if (themeLoading) {
    return (
      <div className="settings-block">
        <div className="settings-body">
          <p className="settings-hint">Loading theme…</p>
        </div>
      </div>
    );
  }

  // Get button style class
  // Rich live preview using the central button style utility
  const previewButtonClasses = useMemo(() => getThemeButtonClasses(
    themeSettings.button_style,
    {
      primary: themeSettings.primary_color,
      primaryHover: themeSettings.primary_hover,
      secondary: themeSettings.secondary_color,
      secondaryHover: themeSettings.secondary_hover,
      tertiary: themeSettings.tertiary_color,
      tertiaryHover: themeSettings.tertiary_hover,
    },
    false
  ), [themeSettings]);

  // Draft primary as CSS variables: the button styles read --color-primary,
  // so previews reflect the colour being picked before it is saved.
  const draftPrimaryVars = {
    '--color-primary': themeSettings.primary_color,
    '--color-primary-hover': themeSettings.primary_hover || generateVariants(themeSettings.primary_color).hover,
    '--color-button-text': getButtonTextColor(themeSettings.primary_color),
  };

  const currentThemeType = detectThemeType(themeSettings);

  return (
    <>
      {/* ===== Primary Theme Archetypes (Apple vs Windows 11) ===== */}
      <div className="settings-block mb-6 overflow-hidden border border-slate-200/80 dark:border-slate-800 shadow-sm">
        <div className="settings-block-heading bg-slate-50/50 dark:bg-slate-900/50">
          <div className="flex items-center gap-3 min-w-0">
            <div className="settings-block-icon text-indigo-500 bg-indigo-500/10">
              <SparklesIcon className="w-5 h-5" />
            </div>
            <div className="min-w-0">
              <div className="flex items-center gap-2">
                <h2 className="settings-block-title text-base">System Design Themes</h2>
                <span className="text-[10px] font-bold px-2 py-0.5 rounded-full bg-indigo-500/10 text-indigo-500 border border-indigo-500/20 uppercase tracking-wider">
                  Core Archetypes
                </span>
              </div>
              <p className="settings-block-note text-xs">
                Switch between the two full design systems. Each theme provides its own background, surface materials, forms, focus indicators, and distinct button elevations.
              </p>
            </div>
          </div>
          {saving && <span className="settings-hint text-xs">Applying theme…</span>}
        </div>

        <div className="p-4 sm:p-6 grid grid-cols-1 md:grid-cols-2 gap-4">
          {/* THEME A: APPLE MACOS */}
          <div
            onClick={() => handleSwitchThemeArchetype('apple')}
            role="button"
            tabIndex={0}
            onKeyDown={(e) => (e.key === 'Enter' || e.key === ' ') && handleSwitchThemeArchetype('apple')}
            className={`cursor-pointer rounded-2xl p-5 border-2 transition-all relative flex flex-col justify-between gap-4 ${
              currentThemeType === 'apple'
                ? 'border-blue-500 bg-blue-500/5 dark:bg-blue-500/10 shadow-md ring-2 ring-blue-500/20'
                : 'border-slate-200 dark:border-slate-800 hover:border-slate-300 dark:hover:border-slate-700 bg-white/60 dark:bg-slate-900/60'
            }`}
          >
            <div>
              <div className="flex items-center justify-between gap-2 mb-2">
                <span className="text-[10px] font-mono font-bold uppercase tracking-wider px-2 py-0.5 rounded bg-blue-500/10 text-blue-600 dark:text-blue-400 border border-blue-500/20 flex items-center gap-1">
                  <span></span>
                  <span>THEME A</span>
                </span>
                {currentThemeType === 'apple' && (
                  <span className="inline-flex items-center gap-1 text-xs font-bold text-blue-600 dark:text-blue-400 bg-blue-100 dark:bg-blue-900/40 px-2 py-0.5 rounded-full">
                    <CheckIcon className="w-3.5 h-3.5" />
                    <span>Active Theme</span>
                  </span>
                )}
              </div>
              <h3 className="text-base font-bold text-slate-900 dark:text-white flex items-center gap-2">
                <span>Apple Native macOS</span>
              </h3>
              <p className="text-xs text-slate-500 dark:text-slate-400 mt-1 leading-relaxed">
                Liquid Glass translucent material (24px frosted blur), 10px squircle geometry, specular top highlight reflection, and spring-based micro-press feedback.
              </p>
            </div>

            {/* Apple Micro Preview */}
            <div className="p-3 rounded-xl bg-slate-100/80 dark:bg-black/40 border border-slate-200/80 dark:border-white/10 space-y-2.5">
              <div className="flex items-center justify-between text-[11px] text-slate-600 dark:text-slate-400 font-medium">
                <span>Preview: 10px squircle & specular sheen</span>
                <span className="flex items-center gap-1">
                  <span className="w-2.5 h-2.5 rounded-full bg-[#007aff]" />
                  <span className="w-2.5 h-2.5 rounded-full bg-[#5856d6]" />
                  <span className="w-2.5 h-2.5 rounded-full bg-[#34c759]" />
                </span>
              </div>
              <div className="flex items-center gap-2">
                <button
                  type="button"
                  onClick={(e) => { e.stopPropagation(); handleSwitchThemeArchetype('apple'); }}
                  className="btn-apple h-8 px-3.5 text-xs text-white font-medium inline-flex items-center gap-1.5"
                >
                  <span></span>
                  <span>Save Record</span>
                </button>
                <span className="btn-apple-secondary h-8 px-3 text-xs inline-flex items-center">
                  Cancel
                </span>
              </div>
            </div>

            <div className="pt-1 flex items-center justify-between">
              <span className="text-[11px] text-slate-400 font-mono">Button Style: apple (10px)</span>
              <button
                type="button"
                onClick={(e) => { e.stopPropagation(); handleSwitchThemeArchetype('apple'); }}
                className={`text-xs font-semibold px-3 py-1 rounded-lg transition ${
                  currentThemeType === 'apple'
                    ? 'bg-blue-600 text-white'
                    : 'bg-slate-200 dark:bg-slate-800 text-slate-700 dark:text-slate-300 hover:bg-slate-300'
                }`}
              >
                {currentThemeType === 'apple' ? 'Active' : 'Apply Apple Theme'}
              </button>
            </div>
          </div>

          {/* THEME B: WINDOWS 11 */}
          <div
            onClick={() => handleSwitchThemeArchetype('windows')}
            role="button"
            tabIndex={0}
            onKeyDown={(e) => (e.key === 'Enter' || e.key === ' ') && handleSwitchThemeArchetype('windows')}
            className={`cursor-pointer rounded-2xl p-5 border-2 transition-all relative flex flex-col justify-between gap-4 ${
              currentThemeType === 'windows'
                ? 'border-sky-500 bg-sky-500/5 dark:bg-sky-500/10 shadow-md ring-2 ring-sky-500/20'
                : 'border-slate-200 dark:border-slate-800 hover:border-slate-300 dark:hover:border-slate-700 bg-white/60 dark:bg-slate-900/60'
            }`}
          >
            <div>
              <div className="flex items-center justify-between gap-2 mb-2">
                <span className="text-[10px] font-mono font-bold uppercase tracking-wider px-2 py-0.5 rounded bg-sky-500/10 text-sky-600 dark:text-sky-400 border border-sky-500/20 flex items-center gap-1">
                  <span>⊞</span>
                  <span>THEME B</span>
                </span>
                {currentThemeType === 'windows' && (
                  <span className="inline-flex items-center gap-1 text-xs font-bold text-sky-600 dark:text-sky-400 bg-sky-100 dark:bg-sky-900/40 px-2 py-0.5 rounded-full">
                    <CheckIcon className="w-3.5 h-3.5" />
                    <span>Active Theme</span>
                  </span>
                )}
              </div>
              <h3 className="text-base font-bold text-slate-900 dark:text-white flex items-center gap-2">
                <span>Windows 11 Fluent</span>
              </h3>
              <p className="text-xs text-slate-500 dark:text-slate-400 mt-1 leading-relaxed">
                WinUI 3 Mica canvas (#F3F3F3 / #202020), crisp 4px/8px geometry, bottom focus accent line on inputs, and signature 3D bottom bevel button elevation.
              </p>
            </div>

            {/* Windows Micro Preview */}
            <div className="p-3 rounded-xl bg-slate-100/80 dark:bg-[#1E1E1E] border border-slate-200/80 dark:border-[#333333] space-y-2.5">
              <div className="flex items-center justify-between text-[11px] text-slate-600 dark:text-slate-400 font-medium">
                <span>Preview: 4px key & 3D bottom bevel</span>
                <span className="flex items-center gap-1">
                  <span className="w-2.5 h-2.5 rounded-sm bg-[#005fb8]" />
                  <span className="w-2.5 h-2.5 rounded-sm bg-[#008272]" />
                  <span className="w-2.5 h-2.5 rounded-sm bg-[#c43e1c]" />
                </span>
              </div>
              <div className="flex items-center gap-2">
                <button
                  type="button"
                  onClick={(e) => { e.stopPropagation(); handleSwitchThemeArchetype('windows'); }}
                  className="btn-windows h-8 px-3.5 text-xs text-white font-semibold inline-flex items-center gap-1.5"
                >
                  <span>⊞</span>
                  <span>Save Record</span>
                </button>
                <span className="btn-windows-secondary h-8 px-3 text-xs inline-flex items-center">
                  Cancel
                </span>
                <Link
                  to="/windows-preview"
                  onClick={(e) => e.stopPropagation()}
                  className="ml-auto text-[11px] text-sky-600 dark:text-sky-400 hover:underline font-semibold flex items-center gap-1"
                >
                  <span>Showcase</span>
                  <ArrowRightIcon className="w-3 h-3" />
                </Link>
              </div>
            </div>

            <div className="pt-1 flex items-center justify-between">
              <span className="text-[11px] text-slate-400 font-mono">Button Style: windows (4px)</span>
              <button
                type="button"
                onClick={(e) => { e.stopPropagation(); handleSwitchThemeArchetype('windows'); }}
                className={`text-xs font-semibold px-3 py-1 rounded-lg transition ${
                  currentThemeType === 'windows'
                    ? 'bg-sky-600 text-white'
                    : 'bg-slate-200 dark:bg-slate-800 text-slate-700 dark:text-slate-300 hover:bg-slate-300'
                }`}
              >
                {currentThemeType === 'windows' ? 'Active' : 'Apply Windows Theme'}
              </button>
            </div>
          </div>
        </div>
      </div>

      {/* ===== Button style ===== */}
      <div className="settings-block">
        <div className="settings-block-heading">
          <div className="flex items-center gap-3 min-w-0">
            <div className="settings-block-icon">
              <Square2StackIcon />
            </div>
            <div className="min-w-0">
              <h2 className="settings-block-title">Button Style</h2>
              <p className="settings-block-note">Corner radius and tactile elevation used by actions across the app.</p>
            </div>
          </div>
          {saving && <span className="settings-hint">Saving…</span>}
        </div>

        <div className="settings-body">
          <div className="settings-options settings-options-5">
            {BUTTON_STYLES.map((style) => {
              const isActive = themeSettings.button_style === style.id;
              const isApple = style.variant === 'apple';
              const isWindows = style.variant === 'windows';
              const isModern = style.variant === 'modern';
              const isNeu = style.variant === 'neumorphic';
              const isSlide = style.variant === 'slide';
              const isCascade = style.variant === 'cascade';
              const isLift = style.variant === 'lift';
              const isOutlined = style.variant === 'outlined';
              return (
                <button
                  key={style.id}
                  type="button"
                  onClick={() => handleButtonStyleChange(style.id)}
                  disabled={disableInputs}
                  aria-pressed={isActive}
                  className={`settings-choice-button ${isActive ? "is-active" : ""}`}
                >
                  {/* Visual preview */}
                  <span className="settings-choice-preview" style={draftPrimaryVars}>
                    {isApple ? (
                      <span className="btn-apple h-8 inline-flex items-center px-3 text-[11px] font-medium text-white shadow-sm">
                         Save
                      </span>
                    ) : isWindows ? (
                      <span className="btn-windows h-8 inline-flex items-center px-3 text-[11px] font-semibold text-white shadow-sm">
                        ⊞ Save
                      </span>
                    ) : isLift ? (
                      <span className="btn-lift h-8 inline-flex items-center px-3 text-[11px]">
                        Save
                      </span>
                    ) : isCascade ? (
                      <span className="btn-cascade h-8 inline-flex items-center px-3 text-[11px]">
                        Save
                      </span>
                    ) : isSlide ? (
                      <span className="btn-slide h-8 inline-flex items-center pl-2 text-[11px] rounded-md">
                        <CheckIcon aria-hidden="true" className="w-3.5 h-3.5" />
                        Save
                      </span>
                    ) : (
                    <span
                      className={`w-8 h-8 flex items-center justify-center text-xs font-medium ${style.className} ${isModern ? 'btn-glass' : isNeu ? 'btn-neu' : isOutlined ? 'border-2' : 'text-white'}`}
                      style={isModern || isNeu ? undefined : {
                        backgroundColor: isOutlined ? 'transparent' : themeSettings.primary_color,
                        borderColor: themeSettings.primary_color,
                        color: isOutlined ? themeSettings.primary_color : 'white',
                      }}
                    >
                      {style.icon}
                    </span>
                    )}
                    <span
                      className={`w-8 h-8 flex items-center justify-center text-xs font-medium ${style.className} ${isApple ? 'btn-apple text-white' : isWindows ? 'btn-windows text-white' : isOutlined ? 'border-2' : 'text-white'}`}
                      style={isModern ? {
                        background: `linear-gradient(135deg, ${themeSettings.secondary_color}, ${themeSettings.secondary_hover || themeSettings.secondary_color})`,
                        color: 'white',
                        boxShadow: `0 4px 10px 0 ${themeSettings.secondary_color}55`,
                        border: '1px solid rgba(255,255,255,0.25)',
                      } : isApple ? {
                        background: themeSettings.secondary_color,
                      } : isWindows ? {
                        background: themeSettings.secondary_color,
                      } : {
                        backgroundColor: isOutlined ? 'transparent' : themeSettings.secondary_color,
                        borderColor: themeSettings.secondary_color,
                        color: isOutlined ? themeSettings.secondary_color : 'white',
                      }}
                    >
                      {style.icon}
                    </span>
                  </span>

                  <span className="settings-option-title">{style.name}</span>
                  <span className="settings-option-tagline">{style.description}</span>

                  {isActive && (
                    <span className="settings-choice-check">
                      <CheckIcon aria-hidden="true" />
                    </span>
                  )}
                </button>
              );
            })}
          </div>
        </div>
      </div>

      {/* ===== Presets ===== */}
      <div className="settings-block">
        <div className="settings-block-heading">
          <div className="flex items-center gap-3 min-w-0">
            <div className="settings-block-icon">
              <PaintBrushIcon />
            </div>
            <div className="min-w-0">
              <h2 className="settings-block-title">Theme Colors</h2>
              <p className="settings-block-note">
                Pick a preset or fine-tune the accent colours below.
              </p>
            </div>
          </div>

          {hasChanges && (
            <button
              type="button"
              onClick={handleSaveTheme}
              disabled={disableInputs || saving}
              className="products-action products-action-primary theme-btn-primary"
            >
              {saving ? 'Saving…' : 'Save theme'}
            </button>
          )}
        </div>

        <div className="settings-body">
          <div className="settings-options settings-options-auto">
            {PRESETS.map((preset) => {
              const isActive = activePreset === preset.name;
              return (
                <button
                  key={preset.name}
                  type="button"
                  onClick={() => applyPreset(preset)}
                  disabled={disableInputs}
                  aria-pressed={isActive}
                  className={`settings-choice-button ${isActive ? "is-active" : ""}`}
                >
                  <span className="settings-choice-swatches">
                    <span style={{ backgroundColor: preset.primary }} />
                    <span style={{ backgroundColor: preset.secondary }} />
                    <span style={{ backgroundColor: preset.tertiary }} />
                  </span>
                  <span className="settings-option-title">{preset.name}</span>
                  {isActive && (
                    <span className="settings-choice-check">
                      <CheckIcon aria-hidden="true" />
                    </span>
                  )}
                </button>
              );
            })}
          </div>
        </div>
      </div>

      {/* ===== Primary colours ===== */}
      <div className="settings-block">
        <div className="settings-block-heading">
          <div className="flex items-center gap-3 min-w-0">
            <span
              className="settings-block-icon"
              style={{ background: themeSettings.primary_color }}
              aria-hidden="true"
            />
            <div className="min-w-0">
              <h2 className="settings-block-title">Primary</h2>
              <p className="settings-block-note">Main accent used for actions and selection.</p>
            </div>
          </div>
        </div>
        <div className="settings-body">
          <div className="settings-fields settings-fields-3">
            <ColorInput
              label="Color"
              color={themeSettings.primary_color}
              onChange={(v) => handleColorChange('primary_color', v)}
              disabled={disableInputs}
            />
            <ColorInput
              label="Hover"
              color={themeSettings.primary_hover || generateVariants(themeSettings.primary_color).hover}
              onChange={(v) => handleColorChange('primary_hover', v)}
              disabled={disableInputs}
            />
            <ColorInput
              label="Light"
              color={themeSettings.primary_light || generateVariants(themeSettings.primary_color).light}
              onChange={(v) => handleColorChange('primary_light', v)}
              disabled={disableInputs}
            />
          </div>
        </div>
      </div>

      {/* ===== Secondary & tertiary ===== */}
      <div className="settings-tiles">
        {/* Secondary */}
        <div className="settings-block">
          <div className="settings-block-heading">
            <div className="flex items-center gap-3 min-w-0">
              <span
                className="settings-block-icon"
                style={{ background: themeSettings.secondary_color }}
                aria-hidden="true"
              />
              <div className="min-w-0">
                <h2 className="settings-block-title">Secondary</h2>
                <p className="settings-block-note">Supporting accent.</p>
              </div>
            </div>
          </div>
          <div className="settings-body">
            <div className="settings-field">
              <ColorInput
                label="Color"
                color={themeSettings.secondary_color}
                onChange={(v) => handleColorChange('secondary_color', v)}
                disabled={disableInputs}
              />
              <ColorInput
                label="Hover"
                color={themeSettings.secondary_hover || generateVariants(themeSettings.secondary_color).hover}
                onChange={(v) => handleColorChange('secondary_hover', v)}
                disabled={disableInputs}
              />
            </div>
          </div>
        </div>

        {/* Tertiary */}
        <div className="settings-block">
          <div className="settings-block-heading">
            <div className="flex items-center gap-3 min-w-0">
              <span
                className="settings-block-icon"
                style={{ background: themeSettings.tertiary_color }}
                aria-hidden="true"
              />
              <div className="min-w-0">
                <h2 className="settings-block-title">Tertiary</h2>
                <p className="settings-block-note">Additional data series.</p>
              </div>
            </div>
          </div>
          <div className="settings-body">
            <div className="settings-field">
              <ColorInput
                label="Color"
                color={themeSettings.tertiary_color}
                onChange={(v) => handleColorChange('tertiary_color', v)}
                disabled={disableInputs}
              />
              <ColorInput
                label="Hover"
                color={themeSettings.tertiary_hover || generateVariants(themeSettings.tertiary_color).hover}
                onChange={(v) => handleColorChange('tertiary_hover', v)}
                disabled={disableInputs}
              />
            </div>
          </div>
        </div>
      </div>

      {/* ===== Quick preview ===== */}
      <div className="settings-block">
        <div className="settings-block-heading">
          <div className="flex items-center gap-3 min-w-0">
            <div className="settings-block-icon">
              <Cog6ToothIcon />
            </div>
            <div className="min-w-0">
              <h2 className="settings-block-title">Preview</h2>
              <p className="settings-block-note">How the accent colours read on actions and chips.</p>
            </div>
          </div>
        </div>
        <div className="settings-body">
          <div className="flex items-center gap-3 flex-wrap" style={draftPrimaryVars}>
            {/* data-btn-preview keeps these showing the unsaved draft colours */}
            <button
              type="button"
              disabled
              data-btn-preview
              className={`px-4 py-2.5 text-sm ${previewButtonClasses.primary.className}`}
              style={previewButtonClasses.primary.style}
            >
              Primary
            </button>
            <button
              type="button"
              disabled
              data-btn-preview
              className={`px-4 py-2.5 text-sm ${previewButtonClasses.secondary.className}`}
              style={previewButtonClasses.secondary.style}
            >
              Secondary
            </button>
            <button
              type="button"
              disabled
              data-btn-preview
              className={`px-4 py-2.5 text-sm ${previewButtonClasses.danger.className}`}
              style={previewButtonClasses.danger.style}
            >
              Danger
            </button>
            <span
              className="settings-chip"
              style={{
                "--settings-chip": themeSettings.primary_color,
                backgroundColor: themeSettings.primary_light || generateVariants(themeSettings.primary_color).light,
                color: themeSettings.primary_color,
              }}
            >
              Chip
            </span>
          </div>
        </div>
      </div>

      {/* ===== Bottom Roaming Mascot (Cube Companion) ===== */}
      <div className="settings-block">
        <div className="settings-block-heading">
          <div className="flex items-center gap-3 min-w-0">
            <div className="settings-block-icon">
              <SparklesIcon />
            </div>
            <div className="min-w-0">
              <h2 className="settings-block-title">Bottom Roaming Mascot</h2>
              <p className="settings-block-note">
                Show or hide the animated Cube mascot at the bottom of the screen. The top navigation bar Guide Bot button remains active.
              </p>
            </div>
          </div>
          <span
            className="settings-chip"
            style={{
              "--settings-chip": bottomMascotEnabled ? "#10b981" : "#64748b",
            }}
          >
            {bottomMascotEnabled ? "ON" : "OFF"}
          </span>
        </div>

        <div className="settings-body">
          <div
            className={`p-4 rounded-xl border flex flex-col sm:flex-row sm:items-center justify-between gap-4 transition-colors ${
              isDark
                ? "bg-slate-800/50 border-slate-700/60"
                : "bg-slate-50/80 border-slate-200/90"
            }`}
          >
            <div className="flex items-center gap-4 min-w-0">
              <div
                className={`w-16 h-16 rounded-2xl border flex items-center justify-center shrink-0 transition-opacity ${
                  isDark
                    ? "bg-slate-900/90 border-slate-700"
                    : "bg-white border-slate-200 shadow-2xs"
                } ${bottomMascotEnabled ? "opacity-100" : "opacity-45 grayscale"}`}
              >
                <GuideBotAvatar
                  className="w-12 h-14"
                  fullBody={true}
                  interactive={false}
                  waving={bottomMascotEnabled}
                />
              </div>
              <div className="min-w-0">
                <label
                  htmlFor="theme-bottom-mascot-switch"
                  className={`text-sm font-bold cursor-pointer block ${
                    isDark ? "text-slate-100" : "text-slate-900"
                  }`}
                >
                  Show Bottom Roaming Cube Mascot
                </label>
                <p className="settings-hint mt-0.5">
                  Turns only the bottom roaming character on or off. The top bar Guide Bot button (<kbd className="px-1.5 py-0.5 text-[11px] font-mono rounded bg-slate-200/80 dark:bg-slate-700">Ctrl+G</kbd>) stays enabled.
                </p>
              </div>
            </div>

            <label
              htmlFor="theme-bottom-mascot-switch"
              className="inline-flex items-center gap-3 cursor-pointer shrink-0 select-none"
            >
              <input
                id="theme-bottom-mascot-switch"
                type="checkbox"
                {...{ switch: "" }}
                checked={bottomMascotEnabled}
                onChange={(e) => handleToggleBottomMascot(e.target.checked)}
                className="sr-only peer"
              />
              <span
                aria-hidden="true"
                className={`relative inline-flex h-7 w-12 shrink-0 items-center rounded-full transition-colors duration-200 ${
                  bottomMascotEnabled
                    ? "bg-emerald-500"
                    : isDark
                    ? "bg-slate-700"
                    : "bg-slate-300"
                }`}
              >
                <span
                  className={`inline-block h-5 w-5 transform rounded-full bg-white shadow-md transition-transform duration-200 ${
                    bottomMascotEnabled ? "translate-x-6" : "translate-x-1"
                  }`}
                />
              </span>
              <span
                className={`text-xs font-extrabold uppercase tracking-wider w-8 ${
                  bottomMascotEnabled
                    ? "text-emerald-600 dark:text-emerald-400"
                    : "text-slate-500 dark:text-slate-400"
                }`}
              >
                {bottomMascotEnabled ? "On" : "Off"}
              </span>
            </label>
          </div>
        </div>
      </div>
    </>
  );
}
