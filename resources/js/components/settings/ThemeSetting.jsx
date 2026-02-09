// resources/js/components/settings/ThemeSetting.jsx
import { useState, useEffect } from "react";
import toast from "react-hot-toast";
import axios from "axios";
import { useTheme } from "@/context/ThemeContext";
import { GlassCard, GlassToolbar } from "@/components/glass.jsx";
import { 
  PaintBrushIcon, 
  Bars3Icon,
  ViewColumnsIcon,
  CheckIcon,
  Cog6ToothIcon
} from "@heroicons/react/24/solid";

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
        theme.tertiary_color !== themeSettings.tertiary_color;
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

      {/* ===== Color Presets (Compact) ===== */}
      <GlassCard>
        <div className="flex items-center gap-2 px-3 py-2 border-b border-gray-100 dark:border-slate-700">
          <PaintBrushIcon className="w-4 h-4 text-violet-500" />
          <h2 className="text-sm font-medium text-gray-900 dark:text-white">Theme Colors</h2>
          {hasChanges && (
            <button
              onClick={handleSaveTheme}
              disabled={disableInputs || saving}
              className="ml-auto px-2 py-1 text-xs font-medium bg-blue-500 text-white rounded hover:bg-blue-600 disabled:opacity-50"
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
        <div className="p-3 flex items-center gap-3">
          <button
            disabled
            className="px-3 py-1.5 text-xs font-medium text-white rounded"
            style={{ backgroundColor: themeSettings.primary_color }}
          >
            Primary
          </button>
          <button
            disabled
            className="px-3 py-1.5 text-xs font-medium text-white rounded"
            style={{ backgroundColor: themeSettings.secondary_color }}
          >
            Secondary
          </button>
          <span
            className="px-2 py-1 text-xs font-medium rounded"
            style={{ backgroundColor: themeSettings.tertiary_color, color: 'white' }}
          >
            Tertiary
          </span>
          <span
            className="px-2 py-1 text-xs font-medium rounded"
            style={{ backgroundColor: themeSettings.primary_light || generateVariants(themeSettings.primary_color).light, color: themeSettings.primary_color }}
          >
            Light
          </span>
        </div>
      </GlassCard>
    </div>
  );
}

