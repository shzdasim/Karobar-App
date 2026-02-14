// resources/js/components/settings/ThemeSetting.jsx
import { useState, useEffect, useMemo } from "react";
import toast from "react-hot-toast";
import axios from "axios";
import { useTheme } from "@/context/ThemeContext";
import { GlassCard, GlassToolbar } from "@/components/glass.jsx";
import { 
  PaintBrushIcon, 
  Bars3Icon,
  ViewColumnsIcon,
  CheckIcon,
  Cog6ToothIcon,
  Square2StackIcon
} from "@heroicons/react/24/solid";
import SidebarTemplateSetting from "./SidebarTemplateSetting";
import TopbarTemplateSetting from "./TopbarTemplateSetting";

// Helper to determine text color based on background brightness
const getContrastText = (hexColor) => {
  hexColor = hexColor.replace('#', '');
  const r = parseInt(hexColor.substring(0, 2), 16);
  const g = parseInt(hexColor.substring(2, 4), 16);
  const b = parseInt(hexColor.substring(4, 6), 16);
  const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
  return luminance > 0.5 ? '#1f2937' : '#ffffff';
};

// Section configuration with color schemes - will use dynamic theme colors
const SECTION_CONFIG = {
  core: {
    key: 'primary',
  },
  management: {
    key: 'secondary',
  },
};

// Button style options
const BUTTON_STYLES = [
  { 
    id: 'rounded', 
    name: 'Rounded', 
    icon: '◼',
    description: 'Modern rounded corners',
    className: 'rounded-lg',
    variant: 'filled'
  },
  { 
    id: 'outlined', 
    name: 'Outlined', 
    icon: '▢',
    description: 'Border with transparent bg',
    className: 'rounded-lg',
    variant: 'outlined'
  },
  { 
    id: 'soft', 
    name: 'Soft', 
    icon: '▣',
    description: 'Medium rounded corners',
    className: 'rounded-xl',
    variant: 'filled'
  },
];

// Helper to get color value from theme
const getThemeColor = (theme, colorKey, variant = 'color') => {
  if (!theme) return '#3b82f6';
  const key = `${colorKey}_${variant}`;
  return theme[key] || '#3b82f6';
};

// Helper to generate section styles from theme
const getSectionStyles = (theme, colorKey) => {
  const baseColor = getThemeColor(theme, colorKey, 'color');
  const hoverColor = getThemeColor(theme, colorKey, 'hover');
  const lightColor = getThemeColor(theme, colorKey, 'light');
  
  return {
    gradient: `from-[${baseColor}] to-[${hoverColor}]`,
    bgLight: `bg-[${lightColor}]`,
    bgDark: `dark:bg-[${lightColor}]`,
    borderColor: `border-[${baseColor}]/30 dark:border-[${baseColor}]/30`,
    iconColor: `text-[${baseColor}] dark:text-[${baseColor}]`,
    ringColor: `ring-[${baseColor}]/30`,
  };
};

// Color picker component
function ColorInput({ label, color, onChange, disabled }) {
  return (
    <div className="relative">
      <label className="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">
        {label}
      </label>
      <div className="flex items-center gap-2">
        <input
          type="color"
          value={color}
          onChange={(e) => onChange(e.target.value)}
          disabled={disabled}
          className="w-8 h-8 rounded-lg border-2 border-gray-200 dark:border-slate-600 cursor-pointer disabled:cursor-not-allowed"
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
          className="flex-1 px-2 py-1.5 text-xs border border-gray-200 dark:border-slate-600 rounded-lg bg-white dark:bg-slate-700 text-gray-900 dark:text-white disabled:opacity-50"
        />
      </div>
    </div>
  );
}

// Color palette preset
const PRESETS = [
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

export default function ThemeSetting({ form: parentForm, setForm, disableInputs }) {
  const { theme, saveTheme, activateTheme, loading: themeLoading } = useTheme();
  
  // Get section styles from theme
  const coreStyles = useMemo(() => getSectionStyles(theme, 'primary'), [theme]);
  const managementStyles = useMemo(() => getSectionStyles(theme, 'secondary'), [theme]);
  
  // Memoize theme colors for performance
  const themeColors = useMemo(() => {
    if (!theme) {
      return {
        primary: '#3b82f6',
        primaryHover: '#2563eb',
        primaryLight: '#dbeafe',
        secondary: '#8b5cf6',
        secondaryHover: '#7c3aed',
        secondaryLight: '#ede9fe',
        tertiary: '#06b6d4',
        tertiaryHover: '#0891b2',
        tertiaryLight: '#cffafe',
      };
    }
    return {
      primary: theme.primary_color || '#3b82f6',
      primaryHover: theme.primary_hover || '#2563eb',
      primaryLight: theme.primary_light || '#dbeafe',
      secondary: theme.secondary_color || '#8b5cf6',
      secondaryHover: theme.secondary_hover || '#7c3aed',
      secondaryLight: theme.secondary_light || '#ede9fe',
      tertiary: theme.tertiary_color || '#06b6d4',
      tertiaryHover: theme.tertiary_hover || '#0891b2',
      tertiaryLight: theme.tertiary_light || '#cffafe',
    };
  }, [theme]);

  // Calculate text colors based on background brightness
  const primaryTextColor = useMemo(() => 
    getContrastText(themeColors.primaryHover || themeColors.primary), 
    [themeColors.primary, themeColors.primaryHover]
  );
  
  const secondaryTextColor = useMemo(() => 
    getContrastText(themeColors.secondaryHover || themeColors.secondary), 
    [themeColors.secondary, themeColors.secondaryHover]
  );
  
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
    button_style: 'rounded',
  });
  const [saving, setSaving] = useState(false);
  const [hasChanges, setHasChanges] = useState(false);
  // Track which preset is active (null means custom colors)
  const [activePreset, setActivePreset] = useState(null);

  // Get navigation style from parent form
  const navigation_style = parentForm?.navigation_style || 'sidebar';

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
        button_style: theme.button_style || 'rounded',
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

  const applyPreset = async (preset) => {
    const v = generateVariants(preset.primary);
    const sv = generateVariants(preset.secondary);
    const tv = generateVariants(preset.tertiary);
    const newThemeSettings = {
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
      button_style: themeSettings.button_style || 'rounded',
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
        button_style: savedTheme.button_style || 'rounded',
      });
      setHasChanges(false);
    } catch (err) {
      console.error('Save error:', err);
      toast.error("Failed to save theme: " + (err.message || 'Unknown error'));
    } finally {
      setSaving(false);
    }
  };

  const handleNavigationChange = (style) => {
    if (!disableInputs) {
      setForm(s => ({ ...s, navigation_style: style }));
      toast.success(`Navigation set to ${style}`);
    } else {
      toast.error("You don't have permission to update settings.");
    }
  };

  if (themeLoading) {
    return <div className="p-4 animate-pulse"><div className="h-20 bg-gray-200 dark:bg-slate-700 rounded-lg"></div></div>;
  }

  // Get button style class
  const getButtonStyleClass = (styleId) => {
    const style = BUTTON_STYLES.find(s => s.id === styleId);
    return style?.className || 'rounded-lg';
  };

  return (
    <div className="p-4 space-y-3">
      {/* ===== Navigation Style (Small Card) ===== */}
      <GlassCard>
        <div className="flex items-center gap-2 px-3 py-2 border-b border-gray-100 dark:border-slate-700">
          <Bars3Icon className="w-4 h-4 text-blue-500" />
          <h2 className="text-sm font-medium text-gray-900 dark:text-white">Navigation Style</h2>
        </div>
        <GlassToolbar className="p-2 gap-2">
          {/* Sidebar Option */}
          <div
            className={`flex-1 p-3 rounded-lg border-2 cursor-pointer transition-all ${
              navigation_style === "sidebar"
                ? "border-blue-500 bg-blue-50/50 dark:bg-blue-900/20"
                : "border-gray-200 dark:border-slate-600 hover:border-gray-300"
            }`}
            onClick={() => handleNavigationChange("sidebar")}
          >
            <div className="flex items-center gap-2">
              <ViewColumnsIcon className={`w-5 h-5 ${navigation_style === "sidebar" ? "text-blue-500" : "text-gray-400"}`} />
              <div>
                <span className="text-xs font-medium text-gray-900 dark:text-white">Sidebar</span>
                <p className="text-[10px] text-gray-500">Left side navigation</p>
              </div>
              {navigation_style === "sidebar" && <CheckIcon className="w-4 h-4 text-blue-500 ml-auto" />}
            </div>
          </div>

          {/* Topbar Option */}
          <div
            className={`flex-1 p-3 rounded-lg border-2 cursor-pointer transition-all ${
              navigation_style === "topbar"
                ? "border-blue-500 bg-blue-50/50 dark:bg-blue-900/20"
                : "border-gray-200 dark:border-slate-600 hover:border-gray-300"
            }`}
            onClick={() => handleNavigationChange("topbar")}
          >
            <div className="flex items-center gap-2">
              <Bars3Icon className={`w-5 h-5 ${navigation_style === "topbar" ? "text-blue-500" : "text-gray-400"}`} />
              <div>
                <span className="text-xs font-medium text-gray-900 dark:text-white">Topbar</span>
                <p className="text-[10px] text-gray-500">Horizontal navigation</p>
              </div>
              {navigation_style === "topbar" && <CheckIcon className="w-4 h-4 text-blue-500 ml-auto" />}
            </div>
          </div>
        </GlassToolbar>
      </GlassCard>

      {/* ===== Sidebar Template Selection ===== */}
      {navigation_style === 'sidebar' && (
        <SidebarTemplateSetting 
          form={parentForm} 
          setForm={setForm} 
          disableInputs={disableInputs} 
        />
      )}

      {/* ===== Topbar Template Selection ===== */}
      {navigation_style === 'topbar' && (
        <TopbarTemplateSetting 
          form={parentForm} 
          setForm={setForm} 
          disableInputs={disableInputs} 
        />
      )}

      {/* ===== Button Style (New Section) ===== */}
      <GlassCard>
        <div className="flex items-center gap-2 px-3 py-2 border-b border-gray-100 dark:border-slate-700">
          <Square2StackIcon className="w-4 h-4 text-emerald-500" />
          <h2 className="text-sm font-medium text-gray-900 dark:text-white">Button Style</h2>
        </div>
        <GlassToolbar className="p-2 gap-2">
          {BUTTON_STYLES.map((style) => {
            const isActive = themeSettings.button_style === style.id;
            return (
              <button
                key={style.id}
                onClick={() => handleButtonStyleChange(style.id)}
                disabled={disableInputs}
                className={`flex-1 p-3 rounded-lg border-2 transition-all ${
                  isActive
                    ? "border-blue-500 bg-blue-50/50 dark:bg-blue-900/20 ring-2 ring-blue-200 dark:ring-blue-800"
                    : "border-gray-200 dark:border-slate-600 hover:border-blue-400"
                }`}
              >
                {/* Visual Preview */}
                <div className="flex justify-center gap-2 mb-2">
                  <div 
                    className={`w-8 h-8 flex items-center justify-center text-xs font-medium shadow-sm ${style.className} ${
                      style.variant === 'outlined' 
                        ? 'border-2 bg-transparent' 
                        : 'text-white'
                    }`}
                    style={{ 
                      backgroundColor: style.variant === 'outlined' ? 'transparent' : themeSettings.primary_color,
                      borderColor: themeSettings.primary_color,
                      color: style.variant === 'outlined' ? themeSettings.primary_color : 'white',
                    }}
                  >
                    {style.icon}
                  </div>
                  <div 
                    className={`w-8 h-8 flex items-center justify-center text-xs font-medium shadow-sm ${style.className} ${
                      style.variant === 'outlined' 
                        ? 'border-2 bg-transparent' 
                        : 'text-white'
                    }`}
                    style={{ 
                      backgroundColor: style.variant === 'outlined' ? 'transparent' : themeSettings.secondary_color,
                      borderColor: themeSettings.secondary_color,
                      color: style.variant === 'outlined' ? themeSettings.secondary_color : 'white',
                    }}
                  >
                    {style.icon}
                  </div>
                </div>
                <span className={`block text-[10px] font-medium text-center ${isActive ? "text-blue-600 dark:text-blue-400" : "text-gray-700 dark:text-gray-300"}`}>
                  {style.name}
                </span>
                {isActive && (
                  <div className="flex justify-center mt-1">
                    <CheckIcon className="w-3 h-3 text-blue-500" />
                  </div>
                )}
              </button>
            );
          })}
        </GlassToolbar>
      </GlassCard>

      {/* ===== Color Presets (Compact) ===== */}
      <GlassCard>
        <div className="flex items-center gap-2 px-3 py-2 border-b border-gray-100 dark:border-slate-700">
          <PaintBrushIcon className="w-4 h-4 text-violet-500" />
          <h2 className="text-sm font-medium text-gray-900 dark:text-white">Theme Colors</h2>
          {hasChanges && (
            <button
              onClick={handleSaveTheme}
              disabled={disableInputs || saving}
              className="ml-auto px-2 py-1 text-xs font-medium rounded transition-all duration-200"
              style={{
                background: `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})`,
                color: primaryTextColor,
                boxShadow: `0 2px 8px 0 ${themeColors.primary}40`,
                opacity: (disableInputs || saving) ? 0.6 : 1,
                cursor: (disableInputs || saving) ? 'not-allowed' : 'pointer'
              }}
            >
              {saving ? '...' : 'Save'}
            </button>
          )}
        </div>
        <GlassToolbar className="p-2 gap-2">
          {PRESETS.map((preset) => {
            const isActive = activePreset === preset.name;
            return (
              <button
                key={preset.name}
                onClick={() => applyPreset(preset)}
                disabled={disableInputs}
                className={`flex-1 p-2 rounded-lg border transition-all ${
                  isActive
                    ? "border-blue-500 bg-blue-50/50 dark:bg-blue-900/20 ring-2 ring-blue-200 dark:ring-blue-800"
                    : "border-gray-200 dark:border-slate-600 hover:border-blue-400"
                }`}
              >
                <div className="flex justify-center -space-x-1 mb-1">
                  <div className="w-4 h-4 rounded-full border border-white dark:border-slate-800" style={{ backgroundColor: preset.primary }} />
                  <div className="w-4 h-4 rounded-full border border-white dark:border-slate-800" style={{ backgroundColor: preset.secondary }} />
                  <div className="w-4 h-4 rounded-full border border-white dark:border-slate-800" style={{ backgroundColor: preset.tertiary }} />
                </div>
                <span className={`block text-[10px] font-medium text-center ${isActive ? "text-blue-600 dark:text-blue-400" : "text-gray-700 dark:text-gray-300"}`}>
                  {preset.name}
                </span>
                {isActive && (
                  <div className="flex justify-center mt-1">
                    <CheckIcon className="w-3 h-3 text-blue-500" />
                  </div>
                )}
              </button>
            );
          })}
        </GlassToolbar>
      </GlassCard>

      {/* ===== Primary Colors (Small Grid) ===== */}
      <GlassCard>
        <div className="flex items-center gap-2 px-3 py-2 border-b border-gray-100 dark:border-slate-700">
          <div className="w-3 h-3 rounded-full" style={{ backgroundColor: themeSettings.primary_color }}></div>
          <h2 className="text-sm font-medium text-gray-900 dark:text-white">Primary</h2>
        </div>
        <div className="p-3 grid grid-cols-3 gap-2">
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
      </GlassCard>

      {/* ===== Secondary & Tertiary (Compact Row) ===== */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
        {/* Secondary */}
        <GlassCard>
          <div className="flex items-center gap-2 px-3 py-2 border-b border-gray-100 dark:border-slate-700">
            <div className="w-3 h-3 rounded-full" style={{ backgroundColor: themeSettings.secondary_color }}></div>
            <h2 className="text-sm font-medium text-gray-900 dark:text-white">Secondary</h2>
          </div>
          <div className="p-3 space-y-2">
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
        </GlassCard>

        {/* Tertiary */}
        <GlassCard>
          <div className="flex items-center gap-2 px-3 py-2 border-b border-gray-100 dark:border-slate-700">
            <div className="w-3 h-3 rounded-full" style={{ backgroundColor: themeSettings.tertiary_color }}></div>
            <h2 className="text-sm font-medium text-gray-900 dark:text-white">Tertiary</h2>
          </div>
          <div className="p-3 space-y-2">
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
        </GlassCard>
      </div>

      {/* ===== Quick Preview ===== */}
      <GlassCard>
        <div className="flex items-center gap-2 px-3 py-2 border-b border-gray-100 dark:border-slate-700">
          <Cog6ToothIcon className="w-4 h-4 text-gray-500" />
          <h2 className="text-sm font-medium text-gray-900 dark:text-white">Preview</h2>
        </div>
        <div className="p-3 flex items-center gap-3 flex-wrap">
          <button
            disabled
            className={`px-3 py-1.5 text-xs font-medium text-white shadow-sm ${getButtonStyleClass(themeSettings.button_style)}`}
            style={{ backgroundColor: themeSettings.primary_color }}
          >
            Primary
          </button>
          <button
            disabled
            className={`px-3 py-1.5 text-xs font-medium text-white shadow-sm ${getButtonStyleClass(themeSettings.button_style)}`}
            style={{ backgroundColor: themeSettings.secondary_color }}
          >
            Secondary
          </button>
          <span
            className={`px-2 py-1 text-xs font-medium shadow-sm ${getButtonStyleClass(themeSettings.buttonStyle)}`}
            style={{ backgroundColor: themeSettings.tertiary_color, color: 'white' }}
          >
            Tertiary
          </span>
          <span
            className={`px-2 py-1 text-xs font-medium shadow-sm ${getButtonStyleClass(themeSettings.button_style)}`}
            style={{ backgroundColor: themeSettings.primary_light || generateVariants(themeSettings.primary_color).light, color: themeSettings.primary_color }}
          >
            Light
          </span>
        </div>
      </GlassCard>
    </div>
  );
}
