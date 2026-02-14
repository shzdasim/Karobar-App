// resources/js/components/TopbarNavigation.jsx
import React from "react";
import { useTheme } from "@/context/ThemeContext";
import {
  ClassicTopbar,
  MiniTopbar,
  ModernTopbar,
  FloatingTopbar,
  GlassTopbar,
  GradientTopbar,
  MinimalTopbar,
  CyberTopbar,
  AuroraTopbar,
  NebulaTopbar,
  ElegantTopbar,
  VibrantTopbar,
} from "@/components/topbar-templates";

// Map template IDs to components
const topbarTemplateComponents = {
  classic: ClassicTopbar,
  mini: MiniTopbar,
  modern: ModernTopbar,
  floating: FloatingTopbar,
  glass: GlassTopbar,
  gradient: GradientTopbar,
  minimal: MinimalTopbar,
  cyber: CyberTopbar,
  aurora: AuroraTopbar,
  nebula: NebulaTopbar,
  elegant: ElegantTopbar,
  vibrant: VibrantTopbar,
};

export default function TopbarNavigation() {
  const { theme } = useTheme();
  const topbarTemplate = theme?.topbar_template || "classic";

  // Get the appropriate template component
  const TopbarTemplateComponent =
    topbarTemplateComponents[topbarTemplate] || ClassicTopbar;

  return <TopbarTemplateComponent />;
}
