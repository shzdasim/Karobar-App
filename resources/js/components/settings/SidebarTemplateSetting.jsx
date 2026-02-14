// resources/js/components/settings/SidebarTemplateSetting.jsx
import { useState, useEffect } from "react";
import toast from "react-hot-toast";
import { useTheme } from "@/context/ThemeContext";
import { GlassCard, GlassToolbar } from "@/components/glass.jsx";
import { 
  ViewColumnsIcon,
  CheckIcon,
  Squares2X2Icon,
} from "@heroicons/react/24/solid";

// Sidebar template options with visual representations
const SIDEBAR_TEMPLATES = [
  { 
    id: 'classic', 
    name: 'Classic', 
    icon: '▤',
    description: 'Traditional sidebar with clear sections',
    preview: 'border-2 border-gray-300 dark:border-gray-600'
  },
  { 
    id: 'mini', 
    name: 'Mini', 
    icon: '▦',
    description: 'Compact icon-only sidebar',
    preview: 'w-8'
  },
  { 
    id: 'modern', 
    name: 'Modern', 
    icon: '▧',
    description: 'Clean, contemporary design',
    preview: 'rounded-xl'
  },
  { 
    id: 'floating', 
    name: 'Floating', 
    icon: '◫',
    description: 'Detached, floating panels',
    preview: 'rounded-2xl shadow-xl'
  },
  { 
    id: 'glass', 
    name: 'Glass', 
    icon: '◈',
    description: 'Glassmorphism effect',
    preview: 'backdrop-blur-md bg-white/30'
  },
  { 
    id: 'gradient', 
    name: 'Gradient', 
    icon: '◐',
    description: 'Gradient backgrounds',
    preview: 'bg-gradient-to-br'
  },
  { 
    id: 'minimal', 
    name: 'Minimal', 
    icon: '○',
    description: 'Ultra-clean minimal design',
    preview: 'border-0'
  },
  { 
    id: 'cyber', 
    name: 'Cyber', 
    icon: '◇',
    description: 'Cyberpunk aesthetic',
    preview: 'bg-slate-900 border-cyan-500'
  },
  { 
    id: 'aurora', 
    name: 'Aurora', 
    icon: '◉',
    description: 'Aurora/northern lights effect',
    preview: 'bg-gradient-to-b from-emerald-500/20'
  },
  { 
    id: 'nebula', 
    name: 'Nebula', 
    icon: '✦',
    description: 'Space/nebula theme',
    preview: 'bg-slate-950'
  },
  { 
    id: 'elegant', 
    name: 'Elegant', 
    icon: '◈',
    description: 'Sophisticated, refined design',
    preview: 'bg-stone-50'
  },
  { 
    id: 'vibrant', 
    name: 'Vibrant', 
    icon: '◉',
    description: 'Bold, colorful design',
    preview: 'bg-gradient-to-br from-orange-50'
  },
];

export default function SidebarTemplateSetting({ form: parentForm, setForm, disableInputs }) {
  const { theme, saveTheme, loading: themeLoading } = useTheme();
  
  const [selectedTemplate, setSelectedTemplate] = useState('classic');
  const [saving, setSaving] = useState(false);

  // Get navigation style from parent form
  const navigationStyle = parentForm?.navigation_style || 'sidebar';

  // Get sidebar template from parent form or theme
  const sidebarTemplate = parentForm?.sidebar_template || theme?.sidebar_template || 'classic';

  // Load saved template from theme - must be before any conditional returns
  useEffect(() => {
    if (theme?.sidebar_template) {
      setSelectedTemplate(theme.sidebar_template);
    }
  }, [theme]);

  // Only show sidebar templates when navigation style is 'sidebar'
  if (navigationStyle !== 'sidebar') {
    return null;
  }

  const handleTemplateChange = async (templateId) => {
    if (disableInputs) {
      toast.error("You don't have permission to update settings.");
      return;
    }

    // Update local state immediately for instant preview
    setSelectedTemplate(templateId);
    
    // Update parent form if available
    if (setForm) {
      setForm(s => ({ ...s, sidebar_template: templateId }));
    }

    // Save to theme settings
    try {
      setSaving(true);
      await saveTheme({ 
        ...theme,
        sidebar_template: templateId 
      });
      toast.success(`Sidebar template: ${SIDEBAR_TEMPLATES.find(t => t.id === templateId)?.name}`);
    } catch (err) {
      console.error('Failed to save sidebar template:', err);
      toast.error("Failed to update sidebar template");
      // Revert on error
      setSelectedTemplate(sidebarTemplate);
    } finally {
      setSaving(false);
    }
  };

  if (themeLoading) {
    return (
      <div className="p-4 animate-pulse">
        <div className="h-20 bg-gray-200 dark:bg-slate-700 rounded-lg"></div>
      </div>
    );
  }

  return (
    <div className="p-4 space-y-3">
      {/* Sidebar Template Selection */}
      <GlassCard>
        <div className="flex items-center gap-2 px-3 py-2 border-b border-gray-100 dark:border-slate-700">
          <ViewColumnsIcon className="w-4 h-4 text-violet-500" />
          <h2 className="text-sm font-medium text-gray-900 dark:text-white">Sidebar Template</h2>
          {saving && <span className="ml-auto text-xs text-gray-500">Saving...</span>}
        </div>
        
        <div className="p-3 grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-3">
          {SIDEBAR_TEMPLATES.map((template) => {
            const isActive = selectedTemplate === template.id;
            return (
              <button
                key={template.id}
                onClick={() => handleTemplateChange(template.id)}
                disabled={disableInputs || saving}
                className={`relative p-3 rounded-xl border-2 transition-all duration-200 text-left ${
                  isActive
                    ? "border-violet-500 bg-violet-50/50 dark:bg-violet-900/20 ring-2 ring-violet-200 dark:ring-violet-800"
                    : "border-gray-200 dark:border-slate-600 hover:border-violet-300 dark:hover:border-violet-700 hover:bg-gray-50 dark:hover:bg-slate-800/50"
                }`}
              >
                {/* Preview Box */}
                <div className={`h-12 mb-2 rounded-lg ${template.preview} bg-gray-100 dark:bg-slate-700 flex items-center justify-center`}>
                  <span className="text-2xl">{template.icon}</span>
                </div>
                
                {/* Template Name */}
                <div className="flex items-center justify-between">
                  <span className={`block text-xs font-medium ${isActive ? "text-violet-700 dark:text-violet-400" : "text-gray-700 dark:text-gray-300"}`}>
                    {template.name}
                  </span>
                  {isActive && <CheckIcon className="w-3 h-3 text-violet-500" />}
                </div>
                
                {/* Description */}
                <p className="text-[10px] text-gray-500 dark:text-gray-400 mt-1 line-clamp-2">
                  {template.description}
                </p>
              </button>
            );
          })}
        </div>
      </GlassCard>

      {/* Current Template Info */}
      <GlassCard>
        <div className="flex items-center gap-2 px-3 py-2 border-b border-gray-100 dark:border-slate-700">
          <Squares2X2Icon className="w-4 h-4 text-emerald-500" />
          <h2 className="text-sm font-medium text-gray-900 dark:text-white">Current Template</h2>
        </div>
        <div className="p-3">
          <div className="flex items-center gap-3">
            <div className="h-12 w-12 rounded-xl bg-gradient-to-br from-violet-500 to-purple-600 flex items-center justify-center text-white text-xl shadow-lg">
              {SIDEBAR_TEMPLATES.find(t => t.id === selectedTemplate)?.icon}
            </div>
            <div>
              <p className="text-sm font-medium text-gray-900 dark:text-white">
                {SIDEBAR_TEMPLATES.find(t => t.id === selectedTemplate)?.name}
              </p>
              <p className="text-xs text-gray-500 dark:text-gray-400">
                {SIDEBAR_TEMPLATES.find(t => t.id === selectedTemplate)?.description}
              </p>
            </div>
          </div>
        </div>
      </GlassCard>
    </div>
  );
}
