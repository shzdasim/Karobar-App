// resources/js/utils/buttonStyles.js

export const BUTTON_RADIUS_MAP = {
  modern: 'rounded-xl',
  rounded: 'rounded-lg',
  'rounded-sm': 'rounded-md',
  soft: 'rounded-2xl',
  pill: 'rounded-full',
  outlined: 'rounded-lg',
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

  if (style === 'outlined') {
    return {
      primary: {
        className: `${radiusClass} border-2 font-medium transition-all duration-200 hover:shadow-xs active:scale-[0.98]`,
        style: {
          borderColor: primary,
          color: primary,
          backgroundColor: 'transparent',
        }
      },
      secondary: {
        className: `${radiusClass} border-2 font-medium transition-all duration-200 hover:shadow-xs active:scale-[0.98]`,
        style: {
          borderColor: secondary,
          color: secondary,
          backgroundColor: 'transparent',
        }
      },
      tertiary: {
        className: `${radiusClass} border-2 font-medium transition-all duration-200 hover:shadow-xs active:scale-[0.98]`,
        style: {
          borderColor: tertiary,
          color: tertiary,
          backgroundColor: 'transparent',
        }
      },
      danger: {
        className: `${radiusClass} border-2 font-medium transition-all duration-200 hover:shadow-xs active:scale-[0.98]`,
        style: {
          borderColor: danger,
          color: danger,
          backgroundColor: 'transparent',
        }
      },
      success: {
        className: `${radiusClass} border-2 font-medium transition-all duration-200 hover:shadow-xs active:scale-[0.98]`,
        style: {
          borderColor: success,
          color: success,
          backgroundColor: 'transparent',
        }
      },
      glass: {
        className: `${radiusClass} border font-medium transition-all duration-200 hover:shadow-xs active:scale-[0.98]`,
        style: {
          backgroundColor: isDark ? 'rgba(30, 41, 59, 0.4)' : 'rgba(255, 255, 255, 0.5)',
          borderColor: isDark ? 'rgba(255, 255, 255, 0.15)' : 'rgba(0, 0, 0, 0.12)',
          color: isDark ? '#f1f5f9' : '#111827',
        }
      },
    };
  }

  if (style === 'modern') {
    return {
      primary: {
        className: `${radiusClass} font-semibold transition-all duration-200 hover:-translate-y-0.5 active:translate-y-0 active:scale-[0.99]`,
        style: {
          background: `linear-gradient(135deg, ${primary}, ${primaryHover})`,
          color: primaryText,
          boxShadow: `0 4px 14px 0 ${primary}45, 0 1px 2px 0 rgba(0,0,0,0.06)`,
          border: '1px solid rgba(255, 255, 255, 0.25)',
        }
      },
      secondary: {
        className: `${radiusClass} font-semibold transition-all duration-200 hover:-translate-y-0.5 active:translate-y-0 active:scale-[0.99]`,
        style: {
          background: `linear-gradient(135deg, ${secondary}, ${secondaryHover})`,
          color: secondaryText,
          boxShadow: `0 4px 14px 0 ${secondary}45, 0 1px 2px 0 rgba(0,0,0,0.06)`,
          border: '1px solid rgba(255, 255, 255, 0.25)',
        }
      },
      tertiary: {
        className: `${radiusClass} font-semibold transition-all duration-200 hover:-translate-y-0.5 active:translate-y-0 active:scale-[0.99]`,
        style: {
          background: `linear-gradient(135deg, ${tertiary}, ${tertiaryHover})`,
          color: tertiaryText,
          boxShadow: `0 4px 14px 0 ${tertiary}45, 0 1px 2px 0 rgba(0,0,0,0.06)`,
          border: '1px solid rgba(255, 255, 255, 0.25)',
        }
      },
      danger: {
        className: `${radiusClass} font-semibold transition-all duration-200 hover:-translate-y-0.5 active:translate-y-0 active:scale-[0.99]`,
        style: {
          background: `linear-gradient(135deg, ${danger}, ${dangerHover})`,
          color: dangerText,
          boxShadow: `0 4px 14px 0 rgba(239, 68, 68, 0.45), 0 1px 2px 0 rgba(0,0,0,0.06)`,
          border: '1px solid rgba(255, 255, 255, 0.25)',
        }
      },
      success: {
        className: `${radiusClass} font-semibold transition-all duration-200 hover:-translate-y-0.5 active:translate-y-0 active:scale-[0.99]`,
        style: {
          background: `linear-gradient(135deg, ${success}, ${successHover})`,
          color: successText,
          boxShadow: `0 4px 14px 0 rgba(16, 185, 129, 0.45), 0 1px 2px 0 rgba(0,0,0,0.06)`,
          border: '1px solid rgba(255, 255, 255, 0.25)',
        }
      },
      glass: {
        className: `${radiusClass} font-medium transition-all duration-200 hover:-translate-y-0.5 active:translate-y-0`,
        style: {
          backgroundColor: isDark ? 'rgba(30, 41, 59, 0.7)' : 'rgba(255, 255, 255, 0.85)',
          color: isDark ? '#f1f5f9' : '#111827',
          backdropFilter: 'blur(8px)',
          border: isDark ? '1px solid rgba(255, 255, 255, 0.12)' : '1px solid rgba(0, 0, 0, 0.08)',
          boxShadow: '0 2px 8px 0 rgba(0, 0, 0, 0.05)',
        }
      },
    };
  }

  // Filled styles for rounded, soft, pill, rounded-sm
  return {
    primary: {
      className: `${radiusClass} font-medium transition-all duration-200 active:scale-[0.98]`,
      style: {
        background: `linear-gradient(to bottom right, ${primary}, ${primaryHover})`,
        color: primaryText,
        boxShadow: `0 4px 14px 0 ${primary}40`,
      }
    },
    secondary: {
      className: `${radiusClass} font-medium transition-all duration-200 active:scale-[0.98]`,
      style: {
        background: `linear-gradient(to bottom right, ${secondary}, ${secondaryHover})`,
        color: secondaryText,
        boxShadow: `0 4px 14px 0 ${secondary}40`,
      }
    },
    tertiary: {
      className: `${radiusClass} font-medium transition-all duration-200 active:scale-[0.98]`,
      style: {
        background: `linear-gradient(to bottom right, ${tertiary}, ${tertiaryHover})`,
        color: tertiaryText,
        boxShadow: `0 4px 14px 0 ${tertiary}40`,
      }
    },
    danger: {
      className: `${radiusClass} font-medium transition-all duration-200 active:scale-[0.98]`,
      style: {
        background: `linear-gradient(to bottom right, ${danger}, ${dangerHover})`,
        color: dangerText,
        boxShadow: '0 4px 14px 0 rgba(239, 68, 68, 0.4)',
      }
    },
    success: {
      className: `${radiusClass} font-medium transition-all duration-200 active:scale-[0.98]`,
      style: {
        background: `linear-gradient(to bottom right, ${success}, ${successHover})`,
        color: successText,
        boxShadow: '0 4px 14px 0 rgba(16, 185, 129, 0.4)',
      }
    },
    glass: {
      className: `${radiusClass} font-medium transition-all duration-200 active:scale-[0.98]`,
      style: {
        backgroundColor: isDark ? 'rgba(51, 65, 85, 0.6)' : 'rgba(255, 255, 255, 0.8)',
        color: isDark ? '#f1f5f9' : '#111827',
        backdropFilter: 'blur(6px)',
        border: isDark ? '1px solid rgba(71, 85, 105, 0.5)' : '1px solid rgba(229, 231, 235, 0.6)',
      }
    },
  };
}
