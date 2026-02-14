// src/components/Sidebar.jsx
import React from "react";
import { useTheme } from "@/context/ThemeContext";

// Import all sidebar templates
import {
  ClassicSidebar,
  MiniSidebar,
  ModernSidebar,
  FloatingSidebar,
  GlassSidebar,
  GradientSidebar,
  MinimalSidebar,
  CyberSidebar,
  AuroraSidebar,
  NebulaSidebar,
  ElegantSidebar,
  VibrantSidebar,
} from "./sidebar-templates";

// Template mapping
const TEMPLATE_COMPONENTS = {
  classic: ClassicSidebar,
  mini: MiniSidebar,
  modern: ModernSidebar,
  floating: FloatingSidebar,
  glass: GlassSidebar,
  gradient: GradientSidebar,
  minimal: MinimalSidebar,
  cyber: CyberSidebar,
  aurora: AuroraSidebar,
  nebula: NebulaSidebar,
  elegant: ElegantSidebar,
  vibrant: VibrantSidebar,
};

export default function Sidebar(props) {
  const { theme, loading } = useTheme();

  // Get the selected template from theme settings
  const selectedTemplate = theme?.sidebar_template || 'classic';

  // Get the appropriate sidebar component
  const SidebarComponent = TEMPLATE_COMPONENTS[selectedTemplate] || ClassicSidebar;

  if (loading) {
    return (
      <aside className="relative h-screen w-64 bg-gray-100 dark:bg-slate-900 animate-pulse">
        <div className="h-full flex flex-col p-4">
          <div className="h-12 bg-gray-200 dark:bg-slate-800 rounded-lg mb-4"></div>
          <div className="flex-1 space-y-2">
            {[...Array(8)].map((_, i) => (
              <div key={i} className="h-10 bg-gray-200 dark:bg-slate-800 rounded-lg"></div>
            ))}
          </div>
        </div>
      </aside>
    );
  }

  return <SidebarComponent {...props} />;
}

// Re-export all templates for direct use if needed
export {
  ClassicSidebar,
  MiniSidebar,
  ModernSidebar,
  FloatingSidebar,
  GlassSidebar,
  GradientSidebar,
  MinimalSidebar,
  CyberSidebar,
  AuroraSidebar,
  NebulaSidebar,
  ElegantSidebar,
  VibrantSidebar,
};

