// resources/js/components/settings/ThemeSetting.jsx
import toast from "react-hot-toast";
import { useTheme } from "@/context/ThemeContext";
import { GlassCard, GlassSectionHeader, GlassToolbar } from "@/components/glass.jsx";
import { Bars3Icon, ViewColumnsIcon } from "@heroicons/react/24/solid";

// Section configuration with color schemes - matching sidebar design
const SECTION_CONFIG = {
  core: {
    gradient: "from-blue-500 to-cyan-600",
    bgLight: "bg-blue-50",
    bgDark: "dark:bg-blue-900/20",
    borderColor: "border-blue-200 dark:border-blue-700",
    iconColor: "text-blue-600 dark:text-blue-400",
    ringColor: "ring-blue-300 dark:ring-blue-700",
  },
  management: {
    gradient: "from-violet-500 to-purple-600",
    bgLight: "bg-violet-50",
    bgDark: "dark:bg-violet-900/20",
    borderColor: "border-violet-200 dark:border-violet-700",
    iconColor: "text-violet-600 dark:text-violet-400",
    ringColor: "ring-violet-300 dark:ring-violet-700",
  },
};

export default function ThemeSetting({ 
  form, 
  setForm, 
  disableInputs
}) {
  const { isDark } = useTheme();

  return (
    <div className="p-4 space-y-3">
      {/* ===== Navigation Style Selection ===== */}
      <GlassCard>
        {/* Header */}
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100 dark:border-slate-700">
          <div className="flex items-center gap-3">
            <div className={`p-2 rounded-lg bg-gradient-to-br ${SECTION_CONFIG.core.gradient} shadow-sm`}>
              <ViewColumnsIcon className="w-5 h-5 text-white" />
            </div>
            <div>
              <h2 className="text-base font-semibold text-gray-900 dark:text-white">Navigation Style</h2>
              <p className="text-xs text-gray-500 dark:text-gray-400">Choose how you want to navigate</p>
            </div>
          </div>
        </div>

        <GlassToolbar className="grid grid-cols-1 md:grid-cols-2 gap-4 p-4 items-stretch">
          {/* Sidebar Option */}
          <div
            className={`relative p-6 rounded-xl border-2 cursor-pointer transition-all h-full flex flex-col ${
              form.navigation_style === "sidebar"
                ? "border-blue-500 bg-blue-50/50 dark:bg-blue-900/20 ring-2 ring-blue-200 dark:ring-blue-800"
                : "border-gray-200 hover:border-gray-300 dark:border-slate-600 dark:hover:border-slate-500"
            }`}
            onClick={() => {
              if (!disableInputs) {
                setForm(s => ({ ...s, navigation_style: "sidebar" }));
              }
            }}
          >
            {form.navigation_style === "sidebar" && (
              <div className="absolute top-3 right-3">
                <div className="w-6 h-6 rounded-full bg-blue-500 flex items-center justify-center">
                  <svg className="w-4 h-4 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="3" d="M5 13l4 4L19 7" />
                  </svg>
                </div>
              </div>
            )}

            <div className="flex items-start gap-4">
              <div className={`p-3 rounded-lg ${form.navigation_style === "sidebar" ? "bg-blue-100 dark:bg-blue-800" : "bg-gray-100 dark:bg-slate-700"}`}>
                <ViewColumnsIcon className={`w-8 h-8 ${form.navigation_style === "sidebar" ? "text-blue-600" : "text-gray-600 dark:text-gray-300"}`} />
              </div>
              <div className="flex-1">
                <h4 className={`font-semibold ${isDark ? "text-slate-100" : "text-gray-800"} text-lg`}>Sidebar Navigation</h4>
                <p className={`text-sm mt-1 ${isDark ? "text-slate-400" : "text-gray-600"}`}>
                  Traditional left sidebar navigation with collapsible menu items grouped by sections.
                </p>
                <div className="flex flex-wrap gap-2 mt-3">
                  <span className={`inline-flex items-center px-2 py-1 rounded text-xs font-medium ${isDark ? "bg-slate-700 text-slate-300" : "bg-gray-100 text-gray-600"}`}>
                    Collapsible
                  </span>
                  <span className={`inline-flex items-center px-2 py-1 rounded text-xs font-medium ${isDark ? "bg-slate-700 text-slate-300" : "bg-gray-100 text-gray-600"}`}>
                    Section Groups
                  </span>
                  <span className={`inline-flex items-center px-2 py-1 rounded text-xs font-medium ${isDark ? "bg-slate-700 text-slate-300" : "bg-gray-100 text-gray-600"}`}>
                    Keyboard Nav
                  </span>
                </div>
              </div>
            </div>

            {/* Preview Placeholder */}
            <div className={`mt-4 p-3 rounded-lg border ${isDark ? "bg-slate-800 border-slate-600" : "bg-white border-gray-200"}`}>
              <div className="flex gap-2">
                <div className={`w-16 ${isDark ? "bg-slate-700" : "bg-gray-100"} rounded h-24 flex items-center justify-center`}>
                  <ViewColumnsIcon className={`w-6 h-6 ${isDark ? "text-slate-500" : "text-gray-400"}`} />
                </div>
                <div className={`flex-1 ${isDark ? "bg-slate-800/50" : "bg-gray-50"} rounded h-24 p-2`}>
                  <div className={`text-xs ${isDark ? "text-slate-500" : "text-gray-400"}`}>Main content area</div>
                </div>
              </div>
            </div>

            <button
              type="button"
              onClick={(e) => {
                e.stopPropagation();
                if (!disableInputs) {
                  setForm(s => ({ ...s, navigation_style: "sidebar" }));
                } else {
                  toast.error("You don't have permission to update settings.");
                }
              }}
              disabled={disableInputs}
              className={`w-full mt-4 py-2.5 px-4 rounded-lg text-sm font-medium transition-all ${
                form.navigation_style === "sidebar"
                  ? "bg-blue-500 text-white"
                  : `${isDark ? "bg-slate-700 text-slate-200 hover:bg-slate-600" : "bg-gray-100 text-gray-700 hover:bg-gray-200"}`
              } ${disableInputs && form.navigation_style !== "sidebar" ? "opacity-50 cursor-not-allowed" : ""}`}
            >
              {form.navigation_style === "sidebar" ? "Selected" : "Select Sidebar"}
            </button>
          </div>

          {/* Topbar Option */}
          <div
            className={`relative p-6 rounded-xl border-2 cursor-pointer transition-all h-full flex flex-col ${
              form.navigation_style === "topbar"
                ? "border-blue-500 bg-blue-50/50 dark:bg-blue-900/20 ring-2 ring-blue-200 dark:ring-blue-800"
                : "border-gray-200 hover:border-gray-300 dark:border-slate-600 dark:hover:border-slate-500"
            }`}
            onClick={() => {
              if (!disableInputs) {
                setForm(s => ({ ...s, navigation_style: "topbar" }));
              }
            }}
          >
            {form.navigation_style === "topbar" && (
              <div className="absolute top-3 right-3">
                <div className="w-6 h-6 rounded-full bg-blue-500 flex items-center justify-center">
                  <svg className="w-4 h-4 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="3" d="M5 13l4 4L19 7" />
                  </svg>
                </div>
              </div>
            )}

            <div className="flex items-start gap-4">
              <div className={`p-3 rounded-lg ${form.navigation_style === "topbar" ? "bg-blue-100 dark:bg-blue-800" : "bg-gray-100 dark:bg-slate-700"}`}>
                <Bars3Icon className={`w-8 h-8 ${form.navigation_style === "topbar" ? "text-blue-600" : "text:text-gray-300-gray-600 dark"}`} />
              </div>
              <div className="flex-1">
                <h4 className={`font-semibold ${isDark ? "text-slate-100" : "text-gray-800"} text-lg`}>Topbar Navigation</h4>
                <p className={`text-sm mt-1 ${isDark ? "text-slate-400" : "text-gray-600"}`}>
                  Horizontal navigation bar at the top of the screen with a horizontal menu below the header.
                </p>
                <div className="flex flex-wrap gap-2 mt-3">
                  <span className={`inline-flex items-center px-2 py-1 rounded text-xs font-medium ${isDark ? "bg-slate-700 text-slate-300" : "bg-gray-100 text-gray-600"}`}>
                    Horizontal Menu
                  </span>
                  <span className={`inline-flex items-center px-2 py-1 rounded text-xs font-medium ${isDark ? "bg-slate-700 text-slate-300" : "bg-gray-100 text-gray-600"}`}>
                    More Space
                  </span>
                  <span className={`inline-flex items-center px-2 py-1 rounded text-xs font-medium ${isDark ? "bg-slate-700 text-slate-300" : "bg-gray-100 text-gray-600"}`}>
                    Scrollable
                  </span>
                </div>
              </div>
            </div>

            {/* Preview Placeholder */}
            <div className={`mt-4 p-3 rounded-lg border ${isDark ? "bg-slate-800 border-slate-600" : "bg-white border-gray-200"}`}>
              <div className="flex flex-col gap-2">
                <div className={`h-8 ${isDark ? "bg-slate-700" : "bg-gray-100"} rounded flex items-center justify-center`}>
                  <Bars3Icon className={`w-5 h-5 ${isDark ? "text-slate-500" : "text-gray-400"}`} />
                </div>
                <div className="flex gap-1 overflow-hidden">
                  <div className={`h-6 ${isDark ? "bg-slate-700" : "bg-gray-50"} rounded flex-1`}></div>
                  <div className={`h-6 ${isDark ? "bg-slate-700" : "bg-gray-50"} rounded flex-1`}></div>
                  <div className={`h-6 ${isDark ? "bg-slate-700" : "bg-gray-50"} rounded flex-1`}></div>
                </div>
                <div className={`h-24 ${isDark ? "bg-slate-700/50" : "bg-gray-50"} rounded flex items-center justify-center`}>
                  <div className={`text-xs ${isDark ? "text-slate-500" : "text-gray-400"}`}>Main content area</div>
                </div>
              </div>
            </div>

            <button
              type="button"
              onClick={(e) => {
                e.stopPropagation();
                if (!disableInputs) {
                  setForm(s => ({ ...s, navigation_style: "topbar" }));
                } else {
                  toast.error("You don't have permission to update settings.");
                }
              }}
              disabled={disableInputs}
              className={`w-full mt-4 py-2.5 px-4 rounded-lg text-sm font-medium transition-all ${
                form.navigation_style === "topbar"
                  ? "bg-blue-500 text-white"
                  : `${isDark ? "bg-slate-700 text-slate-200 hover:bg-slate-600" : "bg-gray-100 text-gray-700 hover:bg-gray-200"}`
              } ${disableInputs && form.navigation_style !== "topbar" ? "opacity-50 cursor-not-allowed" : ""}`}
            >
              {form.navigation_style === "topbar" ? "Selected" : "Select Topbar"}
            </button>
          </div>
        </GlassToolbar>
      </GlassCard>

      {/* ===== Current Selection Info ===== */}
      <div className={`rounded-xl p-4 border ${isDark ? "bg-blue-900/20 border-blue-800" : "bg-blue-50 border-blue-200"}`}>
        <div className="flex items-start gap-3">
          <div className={`p-2 rounded-lg ${isDark ? "bg-blue-800" : "bg-blue-100"}`}>
            {form.navigation_style === "sidebar" ? (
              <ViewColumnsIcon className={`w-5 h-5 ${isDark ? "text-blue-400" : "text-blue-600"}`} />
            ) : (
              <Bars3Icon className={`w-5 h-5 ${isDark ? "text-blue-400" : "text-blue-600"}`} />
            )}
          </div>
          <div>
            <h4 className={`font-medium ${isDark ? "text-blue-300" : "text-blue-800"}`}>
              Current: {form.navigation_style === "sidebar" ? "Sidebar Navigation" : "Topbar Navigation"}
            </h4>
            <p className={`text-sm mt-1 ${isDark ? "text-blue-400" : "text-blue-700"}`}>
              {form.navigation_style === "sidebar"
                ? "The sidebar will be displayed on the left side of the screen. Click the collapse button to minimize it."
                : "A horizontal navigation bar will appear below the top header. The menu items will scroll horizontally if they don't fit."}
            </p>
            {form.navigation_style === "topbar" && (
              <p className={`text-xs mt-2 ${isDark ? "text-blue-400" : "text-blue-600"}`}>
                💡 Tip: In topbar mode, the sidebar will be hidden to give you more screen space.
              </p>
            )}
          </div>
        </div>
      </div>

    </div>
  );
}

