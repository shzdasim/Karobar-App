// resources/js/components/settings/ThemeSetting.jsx
import { useState, useEffect } from "react";
import toast from "react-hot-toast";
import { useTheme } from "@/context/ThemeContext";
import {
  PaintBrushIcon,
  CheckIcon,
  Cog6ToothIcon,
  Square2StackIcon,
} from "@heroicons/react/24/solid";

// Button style options
const BUTTON_STYLES = [
  {
    id: 'rounded-sm',
    name: 'Rounded',
    icon: '◼',
    description: 'Modern rounded-sm corners',
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
    description: 'Medium rounded-sm corners',
    className: 'rounded-xl',
    variant: 'filled'
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
    button_style: 'rounded-sm',
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
        button_style: theme.button_style || 'rounded-sm',
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
      button_style: themeSettings.button_style || 'rounded-sm',
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
        button_style: savedTheme.button_style || 'rounded-sm',
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
  const getButtonStyleClass = (styleId) => {
    const style = BUTTON_STYLES.find(s => s.id === styleId);
    return style?.className || 'rounded-lg';
  };

  return (
    <>
      {/* ===== Button style ===== */}
      <div className="settings-block">
        <div className="settings-block-heading">
          <div className="flex items-center gap-3 min-w-0">
            <div className="settings-block-icon">
              <Square2StackIcon />
            </div>
            <div className="min-w-0">
              <h2 className="settings-block-title">Button Style</h2>
              <p className="settings-block-note">Corner radius used by actions across the app.</p>
            </div>
          </div>
          {saving && <span className="settings-hint">Saving…</span>}
        </div>

        <div className="settings-body">
          <div className="settings-options settings-options-3">
            {BUTTON_STYLES.map((style) => {
              const isActive = themeSettings.button_style === style.id;
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
                  <span className="settings-choice-preview">
                    <span
                      className={`w-8 h-8 flex items-center justify-center text-xs font-medium ${style.className} ${style.variant === 'outlined' ? 'border-2' : 'text-white'}`}
                      style={{
                        backgroundColor: style.variant === 'outlined' ? 'transparent' : themeSettings.primary_color,
                        borderColor: themeSettings.primary_color,
                        color: style.variant === 'outlined' ? themeSettings.primary_color : 'white',
                      }}
                    >
                      {style.icon}
                    </span>
                    <span
                      className={`w-8 h-8 flex items-center justify-center text-xs font-medium ${style.className} ${style.variant === 'outlined' ? 'border-2' : 'text-white'}`}
                      style={{
                        backgroundColor: style.variant === 'outlined' ? 'transparent' : themeSettings.secondary_color,
                        borderColor: themeSettings.secondary_color,
                        color: style.variant === 'outlined' ? themeSettings.secondary_color : 'white',
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
              className="products-action products-action-primary"
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
          <div className="flex items-center gap-3 flex-wrap">
            <button
              type="button"
              disabled
              className={`px-4 py-2.5 text-sm font-medium text-white ${getButtonStyleClass(themeSettings.button_style)}`}
              style={{ backgroundColor: themeSettings.primary_color }}
            >
              Primary
            </button>
            <button
              type="button"
              disabled
              className={`px-4 py-2.5 text-sm font-medium text-white ${getButtonStyleClass(themeSettings.button_style)}`}
              style={{ backgroundColor: themeSettings.secondary_color }}
            >
              Secondary
            </button>
            <span
              className="settings-chip"
              style={{
                "--settings-chip": themeSettings.tertiary_color,
                backgroundColor: themeSettings.tertiary_color,
                color: "#fff",
              }}
            >
              Tertiary
            </span>
            <span
              className="settings-chip"
              style={{
                "--settings-chip": themeSettings.primary_color,
                backgroundColor: themeSettings.primary_light || generateVariants(themeSettings.primary_color).light,
                color: themeSettings.primary_color,
              }}
            >
              Light
            </span>
          </div>
        </div>
      </div>
    </>
  );
}
