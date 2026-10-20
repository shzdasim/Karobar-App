// resources/js/utils/buttonStyles.js

export const BUTTON_RADIUS_MAP = {
  modern: 'rounded-xl',
  rounded: 'rounded-lg',
  'rounded-sm': 'rounded-md',
  soft: 'rounded-2xl',
  pill: 'rounded-[5px]', // Letter Cascade (stored id "pill")
  outlined: 'rounded-[5px]', // Stacked Lift (stored id "outlined")
};

export const getButtonTextColor = (color, hoverColor = null) => {
  const hex = String(color || "").replace("#", "");
  if (hex.length < 6) return "#ffffff";
  const r = parseInt(hex.substring(0, 2), 16);
  const g = parseInt(hex.substring(2, 4), 16);
  const b = parseInt(hex.substring(4, 6), 16);
  const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
  return luminance > 0.5 ? "#1f2937" : "#ffffff";
};

export function getThemeButtonClasses(buttonStyle = 'modern', themeColors = {}, isDark = false) {
  const style = buttonStyle || 'modern';
  const radiusClass = BUTTON_RADIUS_MAP[style] || 'rounded-xl';

  const primary = themeColors.primary || '#3b82f6';
  const primaryHover = themeColors.primaryHover || '#2563eb';
  const primaryText = getButtonTextColor(primary, primaryHover);

  const secondary = themeColors.secondary || '#8b5cf6';
  const secondaryHover = themeColors.secondaryHover || '#7c3aed';
  const secondaryText = getButtonTextColor(secondary, secondaryHover);

  const tertiary = themeColors.tertiary || '#06b6d4';
  const tertiaryHover = themeColors.tertiaryHover || '#0891b2';
  const tertiaryText = getButtonTextColor(tertiary, tertiaryHover);

  const danger = themeColors.danger || '#ef4444';
  const dangerHover = themeColors.dangerHover || '#dc2626';
  const dangerText = getButtonTextColor(danger, dangerHover);

  const success = themeColors.success || themeColors.emerald || '#10b981';
  const successHover = themeColors.successHover || themeColors.emeraldHover || '#059669';
  const successText = getButtonTextColor(success, successHover);

  const styleClassMap = {
    modern: 'btn-glass font-semibold',
    rounded: 'btn-neu font-medium',
    'rounded-sm': 'btn-neu font-medium',
    soft: 'btn-slide font-semibold',
    pill: 'btn-cascade font-bold',
    outlined: 'btn-lift font-medium',
  };
  const activeStyleClass = styleClassMap[style] || 'btn-glass font-semibold';

  const makeRoleVariant = (roleClass, color, hoverColor, inkColor) => ({
    className: `${radiusClass} ${roleClass} ${activeStyleClass}`,
    style: color
      ? {
          '--btn-accent': color,
          '--btn-accent-hover': hoverColor || color,
          '--btn-accent-ink': inkColor,
        }
      : {},
  });

  const successVariant = makeRoleVariant('theme-btn-success', (themeColors.success || themeColors.emerald) ? success : null, successHover, successText);
  const glassVariant = makeRoleVariant('theme-btn-secondary', themeColors.secondary ? secondary : null, secondaryHover, secondaryText);

  return {
    primary: makeRoleVariant('theme-btn-primary', themeColors.primary ? primary : null, primaryHover, primaryText),
    secondary: makeRoleVariant('theme-btn-secondary', themeColors.secondary ? secondary : null, secondaryHover, secondaryText),
    tertiary: makeRoleVariant('theme-btn-tertiary', themeColors.tertiary ? tertiary : null, tertiaryHover, tertiaryText),
    danger: makeRoleVariant('theme-btn-danger', themeColors.danger ? danger : null, dangerHover, dangerText),
    success: successVariant,
    emerald: successVariant,
    glass: glassVariant,
    outlined: glassVariant,
  };
}

