// resources/js/components/settings/TopbarTemplateSetting.jsx
import { useState, useEffect, useMemo } from "react";
import toast from "react-hot-toast";
import { useTheme } from "@/context/ThemeContext";
import { GlassCard, GlassToolbar } from "@/components/glass.jsx";
import { ViewColumnsIcon, CheckIcon } from "@heroicons/react/24/solid";
import { topbarTemplateMetadata } from "@/components/topbar-templates";

// Helper to determine text color based on background brightness
const getContrastText = (hexColor) => {
  hexColor = hexColor.replace('#', '');
  const r = parseInt(hexColor.substring(0, 2), 16);
  const g = parseInt(hexColor.substring(2, 4), 16);
  const b = parseInt(hexColor.substring(4, 6), 16);
  const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
  return luminance > 0.5 ? '#1f2937' : '#ffffff';
};

export default function TopbarTemplateSetting({ form: parentForm, setForm, disableInputs }) {
  const { theme, saveTheme, loading: themeLoading } = useTheme();
  
  // Get theme colors
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

  const primaryTextColor = useMemo(() => 
    getContrastText(themeColors.primaryHover || themeColors.primary), 
    [themeColors.primary, themeColors.primaryHover]
  );

  const [saving, setSaving] = useState(false);
  const [hasChanges, setHasChanges] = useState(false);
  const [topbarTemplate, setTopbarTemplate] = useState('classic');

  // Get navigation style from parent form
  const navigationStyle = parentForm?.navigation_style || 'sidebar';

  // Load saved topbar template from context
  useEffect(() => {
    if (theme && theme.topbar_template) {
      setTopbarTemplate(theme.topbar_template);
      setHasChanges(false);
    }
  }, [theme]);

  // Track changes
  useEffect(() => {
    if (theme && theme.topbar_template) {
      const isChanged = theme.topbar_template !== topbarTemplate;
      setHasChanges(isChanged);
    }
  }, [topbarTemplate, theme]);

  const handleTopbarTemplateChange = async (templateId) => {
    if (disableInputs) {
      toast.error("You don't have permission to update settings.");
      return;
    }
    
    // Update local state immediately for instant preview
    setTopbarTemplate(templateId);
    
    // Save immediately for instant effect
    try {
      setSaving(true);
      const updatedTheme = { ...theme, topbar_template: templateId };
      await saveTheme(updatedTheme);
      toast.success(`Topbar template: ${topbarTemplateMetadata.find(t => t.id === templateId)?.name || templateId}`);
      setHasChanges(false);
    } catch (err) {
      toast.error("Failed to update topbar template");
    } finally {
      setSaving(false);
    }
  };

  // Only show when topbar navigation is selected
  if (navigationStyle !== 'topbar') {
    return null;
  }

  if (themeLoading) {
    return <div className="p-4 animate-pulse"><div className="h-20 bg-gray-200 dark:bg-slate-700 rounded-lg"></div></div>;
  }

  return (
    <GlassCard>
      <div className="flex items-center gap-2 px-3 py-2 border-b border-gray-100 dark:border-slate-700">
        <ViewColumnsIcon className="w-4 h-4 text-purple-500" />
        <h2 className="text-sm font-medium text-gray-900 dark:text-white">Topbar Template</h2>
        {hasChanges && (
          <span className="ml-auto text-xs text-amber-500 font-medium">Unsaved changes</span>
        )}
      </div>
      <GlassToolbar className="p-2 gap-2 flex-wrap">
        {topbarTemplateMetadata.map((template) => {
          const isActive = topbarTemplate === template.id;
          return (
            <button
              key={template.id}
              onClick={() => handleTopbarTemplateChange(template.id)}
              disabled={disableInputs || saving}
              className={`flex-1 min-w-[120px] p-3 rounded-lg border-2 transition-all ${
                isActive
                  ? "border-purple-500 bg-purple-50/50 dark:bg-purple-900/20 ring-2 ring-purple-200 dark:ring-purple-800"
                  : "border-gray-200 dark:border-slate-600 hover:border-purple-400"
              }`}
            >
              {/* Visual Preview */}
              <div className="flex justify-center mb-2">
                <div 
                  className="w-10 h-8 rounded flex items-center justify-center text-lg shadow-sm"
                  style={{ 
                    background: isActive 
                      ? `linear-gradient(to bottom right, ${themeColors.primary}, ${themeColors.primaryHover})`
                      : '#f3f4f6',
                    color: isActive ? primaryTextColor : '#6b7280',
                  }}
                >
                  {template.icon}
                </div>
              </div>
              <span className={`block text-xs font-medium text-center ${isActive ? "text-purple-600 dark:text-purple-400" : "text-gray-700 dark:text-gray-300"}`}>
                {template.name}
              </span>
              <span className="block text-[9px] text-gray-500 dark:text-gray-400 text-center mt-0.5 line-clamp-1">
                {template.description}
              </span>
              {isActive && (
                <div className="flex justify-center mt-1">
                  <CheckIcon className="w-3 h-3 text-purple-500" />
                </div>
              )}
            </button>
          );
        })}
      </GlassToolbar>
    </GlassCard>
  );
}

