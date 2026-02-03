import { Bars3Icon, ViewColumnsIcon } from "@heroicons/react/24/solid";
import { GlassCard, GlassSectionHeader, GlassToolbar, GlassBtn } from "@/components/glass.jsx";

export default function NavigationSetting({ 
  form, 
  setForm, 
  disableInputs, 
  saving, 
  handleSave,
  tintBlue,
  tintGlass,
  tintGreen
}) {
  return (
    <>
      {/* ===== Navigation Style Selection ===== */}
      <GlassCard>
        <GlassSectionHeader
          title={<span className="inline-flex items-center gap-2">
            <ViewColumnsIcon className="w-5 h-5 text-blue-600" />
            <span>Navigation Style</span>
          </span>}
          subtitle="Choose how you want to navigate through the application"
        />
        <GlassToolbar className="grid grid-cols-1 md:grid-cols-2 gap-4 items-stretch">
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
                <h4 className="font-semibold text-gray-800 dark:text-gray-100 text-lg">Sidebar Navigation</h4>
                <p className="text-sm text-gray-600 dark:text-gray-400 mt-1">
                  Traditional left sidebar navigation with collapsible menu items grouped by sections.
                </p>
                <div className="mt-3 flex flex-wrap gap-2">
                  <span className="inline-flex items-center px-2 py-1 rounded text-xs font-medium bg-gray-100 text-gray-600 dark:bg-slate-700 dark:text-gray-300">
                    Collapsible
                  </span>
                  <span className="inline-flex items-center px-2 py-1 rounded text-xs font-medium bg-gray-100 text-gray-600 dark:bg-slate-700 dark:text-gray-300">
                    Section Groups
                  </span>
                  <span className="inline-flex items-center px-2 py-1 rounded text-xs font-medium bg-gray-100 text-gray-600 dark:bg-slate-700 dark:text-gray-300">
                    Keyboard Nav
                  </span>
                </div>
              </div>
            </div>

            {/* Preview Image Placeholder */}
            <div className="mt-4 p-3 bg-white dark:bg-slate-800 rounded-lg border border-gray-200 dark:border-slate-600">
              <div className="flex gap-2">
                <div className="w-16 bg-gray-100 dark:bg-slate-700 rounded h-24 flex items-center justify-center">
                  <ViewColumnsIcon className="w-6 h-6 text-gray-400" />
                </div>
                <div className="flex-1 bg-gray-50 dark:bg-slate-800/50 rounded h-24 p-2">
                  <div className="text-xs text-gray-400 dark:text-gray-500">Main content area</div>
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
                  : "bg-gray-100 text-gray-700 hover:bg-gray-200 dark:bg-slate-700 dark:text-gray-200 dark:hover:bg-slate-600"
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
                <Bars3Icon className={`w-8 h-8 ${form.navigation_style === "topbar" ? "text-blue-600" : "text-gray-600 dark:text-gray-300"}`} />
              </div>
              <div className="flex-1">
                <h4 className="font-semibold text-gray-800 dark:text-gray-100 text-lg">Topbar Navigation</h4>
                <p className="text-sm text-gray-600 dark:text-gray-400 mt-1">
                  Horizontal navigation bar at the top of the screen with a horizontal menu below the header.
                </p>
                <div className="mt-3 flex flex-wrap gap-2">
                  <span className="inline-flex items-center px-2 py-1 rounded text-xs font-medium bg-gray-100 text-gray-600 dark:bg-slate-700 dark:text-gray-300">
                    Horizontal Menu
                  </span>
                  <span className="inline-flex items-center px-2 py-1 rounded text-xs font-medium bg-gray-100 text-gray-600 dark:bg-slate-700 dark:text-gray-300">
                    More Space
                  </span>
                  <span className="inline-flex items-center px-2 py-1 rounded text-xs font-medium bg-gray-100 text-gray-600 dark:bg-slate-700 dark:text-gray-300">
                    Scrollable
                  </span>
                </div>
              </div>
            </div>

            {/* Preview Image Placeholder */}
            <div className="mt-4 p-3 bg-white dark:bg-slate-800 rounded-lg border border-gray-200 dark:border-slate-600">
              <div className="flex flex-col gap-2">
                <div className="h-8 bg-gray-100 dark:bg-slate-700 rounded flex items-center justify-center">
                  <Bars3Icon className="w-5 h-5 text-gray-400" />
                </div>
                <div className="flex gap-1 overflow-hidden">
                  <div className="h-6 bg-gray-50 dark:bg-slate-800/50 rounded flex-1"></div>
                  <div className="h-6 bg-gray-50 dark:bg-slate-800/50 rounded flex-1"></div>
                  <div className="h-6 bg-gray-50 dark:bg-slate-800/50 rounded flex-1"></div>
                </div>
                <div className="bg-gray-50 dark:bg-slate-800/50 rounded h-24 flex items-center justify-center">
                  <div className="text-xs text-gray-400 dark:text-gray-500">Main content area</div>
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
                  : "bg-gray-100 text-gray-700 hover:bg-gray-200 dark:bg-slate-700 dark:text-gray-200 dark:hover:bg-slate-600"
              } ${disableInputs && form.navigation_style !== "topbar" ? "opacity-50 cursor-not-allowed" : ""}`}
            >
              {form.navigation_style === "topbar" ? "Selected" : "Select Topbar"}
            </button>
          </div>
        </GlassToolbar>
      </GlassCard>

      {/* ===== Current Selection Info ===== */}
      <div className="bg-blue-50 dark:bg-blue-900/30 rounded-xl p-4 border border-blue-200 dark:border-blue-800">
        <div className="flex items-start gap-3">
          <div className="p-2 rounded-lg bg-blue-100 dark:bg-blue-800">
            {form.navigation_style === "sidebar" ? (
              <ViewColumnsIcon className="w-5 h-5 text-blue-600 dark:text-blue-400" />
            ) : (
              <Bars3Icon className="w-5 h-5 text-blue-600 dark:text-blue-400" />
            )}
          </div>
          <div>
            <h4 className="font-medium text-blue-800 dark:text-blue-300">
              Current: {form.navigation_style === "sidebar" ? "Sidebar Navigation" : "Topbar Navigation"}
            </h4>
            <p className="text-sm text-blue-700 dark:text-blue-400 mt-1">
              {form.navigation_style === "sidebar"
                ? "The sidebar will be displayed on the left side of the screen. Click the collapse button to minimize it."
                : "A horizontal navigation bar will appear below the top header. The menu items will scroll horizontally if they don't fit."}
            </p>
            {form.navigation_style === "topbar" && (
              <p className="text-xs text-blue-600 dark:text-blue-400 mt-2">
                💡 Tip: In topbar mode, the sidebar will be hidden to give you more screen space.
              </p>
            )}
          </div>
        </div>
      </div>

      {/* ===== Save Button for Navigation Settings ===== */}
      <div className="flex justify-end">
        <GlassBtn
          onClick={handleSave}
          disabled={disableInputs || saving}
          className={`h-10 px-6 ${(disableInputs || saving) ? tintGlass + " opacity-60 cursor-not-allowed" : tintGreen}`}
          title={!disableInputs ? "Alt+S" : "You lack update permission"}
        >
          {saving ? "Saving…" : "Save Settings"}
        </GlassBtn>
      </div>
    </>
  );
}

