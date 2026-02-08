// resources/js/components/settings/PrinterSetting.jsx
import { useState } from "react";
import toast from "react-hot-toast";
import { useTheme } from "@/context/ThemeContext";
import { GlassCard, GlassSectionHeader, GlassToolbar, GlassInput, GlassBtn } from "@/components/glass.jsx";
import { 
  PrinterIcon, 
  DocumentTextIcon,
  EyeIcon,
  DocumentIcon,
  ClipboardDocumentListIcon,
  ScaleIcon,
  BoltIcon,
  QrCodeIcon
} from "@heroicons/react/24/solid";

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

export default function PrinterSetting({ 
  form, 
  handleChange, 
  disableInputs, 
  saving,
  handleSave
}) {
  const { isDark } = useTheme();
  const [selectedThermalTemplate, setSelectedThermalTemplate] = useState(form.thermal_template || "standard");
  const [showPreviewModal, setShowPreviewModal] = useState(false);
  const [previewingTemplate, setPreviewingTemplate] = useState(null);

  // 🎨 Modern button palette
  const btnOutline = "bg-transparent text-slate-600 dark:text-gray-300 ring-1 ring-gray-300 dark:ring-slate-600 hover:bg-gray-100 dark:hover:bg-slate-700/50 transition-all duration-200";
  const btnBlue   = "bg-gradient-to-br from-blue-500 to-blue-600 text-white shadow-lg shadow-blue-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-blue-500/30 hover:scale-[1.02] transition-all duration-200";
  const btnGreen  = "bg-gradient-to-br from-emerald-500 to-emerald-600 text-white shadow-lg shadow-emerald-500/25 ring-1 ring-white/20 hover:shadow-xl hover:shadow-emerald-500/30 hover:scale-[1.02] transition-all duration-200";

  // Thermal templates data
  const thermalTemplates = [
    { 
      id: 'standard', 
      name: 'Standard', 
      description: 'Classic layout with logo support',
      icon: 'DocumentTextIcon',
      preview: 'Standard thermal layout with store branding'
    },
    { 
      id: 'minimal', 
      name: 'Minimal', 
      description: 'No logo, basic info only',
      icon: 'DocumentIcon',
      preview: 'Compact receipt without logo'
    },
    { 
      id: 'detailed', 
      name: 'Detailed', 
      description: 'Extended customer & payment info',
      icon: 'ClipboardDocumentListIcon',
      preview: 'Complete with customer balance details'
    },
    { 
      id: 'compact', 
      name: 'Compact', 
      description: 'Small fonts, more items per page',
      icon: 'ScaleIcon',
      preview: 'Maximum items on single receipt'
    },
    { 
      id: 'bold', 
      name: 'Bold', 
      description: 'Large fonts, high emphasis',
      icon: 'BoltIcon',
      preview: 'Large fonts with black/white contrast'
    },
    { 
      id: 'barcode', 
      name: 'Barcode', 
      description: 'With product barcodes & QR code',
      icon: 'QrCodeIcon',
      preview: 'Includes barcodes and verification QR'
    },
  ];

  const handleTemplateSelect = (templateId) => {
    if (!disableInputs) {
      setSelectedThermalTemplate(templateId);
      handleChange({ target: { name: 'thermal_template', value: templateId } });
    }
  };

  const getIconComponent = (iconName) => {
    const icons = {
      DocumentTextIcon,
      DocumentIcon,
      ClipboardDocumentListIcon,
      ScaleIcon,
      BoltIcon,
      QrCodeIcon,
    };
    return icons[iconName] || DocumentTextIcon;
  };

  return (
    <div className="p-4 space-y-3">
      {/* ===== Invoice Footer Note ===== */}
      <GlassCard>
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100 dark:border-slate-700">
          <div className="flex items-center gap-3">
            <div className={`p-2 rounded-lg bg-gradient-to-br ${SECTION_CONFIG.core.gradient} shadow-sm`}>
              <DocumentTextIcon className="w-5 h-5 text-white" />
            </div>
            <div>
              <h2 className="text-base font-semibold text-gray-900 dark:text-white">Invoice Footer Note</h2>
              <p className="text-xs text-gray-500 dark:text-gray-400">Custom message at bottom of invoices</p>
            </div>
          </div>
        </div>
        <div className="p-4">
          <textarea
            name="note"
            value={form.note}
            onChange={handleChange}
            disabled={disableInputs}
            rows={3}
            onKeyDown={(e) => {
              if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) {
                e.preventDefault();
                if (!disableInputs) handleSave();
              }
            }}
            className={`w-full h-24 rounded-xl bg-white/70 backdrop-blur-sm border focus:outline-none focus:ring-2 focus:ring-blue-400/40 shadow-sm px-3 py-2 ${
              isDark 
                ? "bg-slate-700/70 border-slate-600/70 text-gray-100" 
                : "bg-white border-gray-200/70 text-gray-900"
            }`}
            placeholder="This note will be printed at the bottom of the invoice..."
          />
          <p className={`text-xs mt-2 ${isDark ? "text-slate-400" : "text-gray-500"}`}>
            This note will appear at the bottom of all printed invoices and receipts.
          </p>
        </div>
      </GlassCard>

      {/* ===== Printer Type Selection ===== */}
      <GlassCard>
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100 dark:border-slate-700">
          <div className="flex items-center gap-3">
            <div className={`p-2 rounded-lg bg-gradient-to-br ${SECTION_CONFIG.management.gradient} shadow-sm`}>
              <PrinterIcon className="w-5 h-5 text-white" />
            </div>
            <div>
              <h2 className="text-base font-semibold text-gray-900 dark:text-white">Printer Type</h2>
              <p className="text-xs text-gray-500 dark:text-gray-400">Select your printer type</p>
            </div>
          </div>
        </div>
        <GlassToolbar className="flex flex-wrap gap-3 p-4">
          <label className={`inline-flex items-center gap-3 px-4 py-3 rounded-xl ring-1 cursor-pointer transition-all ${
            form.printer_type === "thermal" 
              ? isDark ? "bg-blue-900/30 ring-blue-700" : "bg-blue-50 ring-blue-300"
              : isDark ? "bg-slate-700/60 ring-slate-600" : "bg-white/70 ring-gray-200"
          }`}>
            <input
              type="radio"
              name="printer_type"
              value="thermal"
              checked={form.printer_type === "thermal"}
              onChange={handleChange}
              disabled={disableInputs}
              className="w-4 h-4 text-blue-600"
            />
            <div className="flex items-center gap-2">
              <PrinterIcon className={`w-5 h-5 ${isDark ? "text-slate-300" : "text-gray-600"}`} />
              <span className={`text-sm font-medium ${isDark ? "text-slate-200" : "text-gray-700"}`}>Thermal Printer</span>
            </div>
          </label>
          
          <label className={`inline-flex items-center gap-3 px-4 py-3 rounded-xl ring-1 cursor-pointer transition-all ${
            form.printer_type === "a4" 
              ? isDark ? "bg-blue-900/30 ring-blue-700" : "bg-blue-50 ring-blue-300"
              : isDark ? "bg-slate-700/60 ring-slate-600" : "bg-white/70 ring-gray-200"
          }`}>
            <input
              type="radio"
              name="printer_type"
              value="a4"
              checked={form.printer_type === "a4"}
              onChange={handleChange}
              disabled={disableInputs}
              className="w-4 h-4 text-blue-600"
            />
            <div className="flex items-center gap-2">
              <DocumentTextIcon className={`w-5 h-5 ${isDark ? "text-slate-300" : "text-gray-600"}`} />
              <span className={`text-sm font-medium ${isDark ? "text-slate-200" : "text-gray-700"}`}>A4 Printer</span>
            </div>
          </label>
        </GlassToolbar>
        <div className="px-4 pb-4">
          <p className={`text-xs ${isDark ? "text-slate-400" : "text-gray-500"}`}>
            {form.printer_type === "thermal" 
              ? "Select a thermal receipt template below. Thermal printers use 58mm-80mm width paper."
              : "A4 printer settings will be available in a future update."}
          </p>
        </div>
      </GlassCard>

      {/* ===== Thermal Template Selection ===== */}
      {form.printer_type === "thermal" && (
        <GlassCard>
          <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100 dark:border-slate-700">
            <div className="flex items-center gap-3">
              <div className={`p-2 rounded-lg bg-gradient-to-br ${SECTION_CONFIG.core.gradient} shadow-sm`}>
                <DocumentTextIcon className="w-5 h-5 text-white" />
              </div>
              <div>
                <h2 className="text-base font-semibold text-gray-900 dark:text-white">Thermal Receipt Template</h2>
                <p className="text-xs text-gray-500 dark:text-gray-400">Choose your receipt layout</p>
              </div>
            </div>
          </div>
          
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3 p-4">
            {thermalTemplates.map((template) => {
              const IconComponent = getIconComponent(template.icon);
              const isSelected = selectedThermalTemplate === template.id;
              
              return (
                <div
                  key={template.id}
                  className={`relative rounded-xl border-2 cursor-pointer overflow-hidden transition-all ${
                    isSelected
                      ? "border-blue-500 ring-2 ring-blue-200 dark:ring-blue-800"
                      : "border-gray-200 hover:border-gray-300 hover:shadow-md dark:border-slate-600 dark:hover:border-slate-500"
                  }`}
                  onClick={() => handleTemplateSelect(template.id)}
                >
                  {/* Selection Indicator */}
                  {isSelected && (
                    <div className="absolute top-2 right-2 z-10">
                      <div className="w-6 h-6 rounded-full bg-blue-500 flex items-center justify-center">
                        <svg className="w-4 h-4 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="3" d="M5 13l4 4L19 7" />
                        </svg>
                      </div>
                    </div>
                  )}
                  
                  {/* Preview Button */}
                  <button
                    type="button"
                    onClick={(e) => {
                      e.stopPropagation();
                      setPreviewingTemplate(template);
                      setShowPreviewModal(true);
                    }}
                    className="absolute top-2 left-2 z-10 p-1.5 rounded-lg bg-white/90 hover:bg-white shadow-sm transition-opacity dark:bg-slate-700/90 dark:hover:bg-slate-600"
                    title="Preview template"
                  >
                    <EyeIcon className={`w-4 h-4 ${isDark ? "text-slate-300" : "text-gray-600"}`} />
                  </button>
                  
                  {/* Template Content */}
                  <div className="p-4">
                    {/* Small Thumbnail Preview */}
                    <div className={`mb-3 rounded-lg border overflow-hidden ${isDark ? "bg-slate-800 border-slate-600" : "bg-white border-gray-200"}`}>
                      <iframe
                        src={`/print/thermal-preview/${template.id}`}
                        className="w-full h-24 border-0"
                        style={{ transform: 'scale(0.5)', transformOrigin: 'top left', width: '200%', height: '200%' }}
                        title={`${template.name} Thumbnail`}
                      />
                    </div>
                    
                    {/* Icon and Name */}
                    <div className="flex items-start gap-3 mb-3">
                      <div className={`p-2 rounded-lg ${isSelected ? "bg-blue-100 dark:bg-blue-900/50" : "bg-gray-100 dark:bg-slate-700"}`}>
                        <IconComponent className={`w-6 h-6 ${isSelected ? "text-blue-600" : isDark ? "text-slate-300" : "text-gray-600"}`} />
                      </div>
                      <div className="flex-1">
                        <h4 className={`font-semibold ${isDark ? "text-slate-100" : "text-gray-800"}`}>{template.name}</h4>
                        <p className={`text-xs mt-1 ${isDark ? "text-slate-400" : "text-gray-500"}`}>{template.description}</p>
                      </div>
                    </div>
                    
                    {/* Preview Info */}
                    <div className={`rounded-lg p-2 mb-3 ${isDark ? "bg-slate-700/50" : "bg-gray-50"}`}>
                      <p className={`text-xs ${isDark ? "text-slate-300" : "text-gray-600"}`}>{template.preview}</p>
                    </div>
                    
                    {/* Select Button */}
                    <button
                      type="button"
                      onClick={(e) => {
                        e.stopPropagation();
                        if (!disableInputs) {
                          handleTemplateSelect(template.id);
                        } else {
                          toast.error("You don't have permission to update settings.");
                        }
                      }}
                      disabled={disableInputs}
                      className={`w-full py-2 px-3 rounded-lg text-sm font-medium transition-all ${
                        isSelected
                          ? "bg-blue-500 text-white"
                          : `${isDark ? "bg-slate-700 text-slate-200 hover:bg-slate-600" : "bg-gray-100 text-gray-700 hover:bg-gray-200"}`
                      } ${disableInputs && !isSelected ? "opacity-50 cursor-not-allowed" : ""}`}
                    >
                      {isSelected ? "Selected" : "Select Template"}
                    </button>
                  </div>
                </div>
              );
            })}
          </div>
          
          {/* Selected Template Info */}
          <div className="px-4 pb-4">
            <div className={`rounded-xl p-4 border ${isDark ? "bg-blue-900/20 border-blue-800" : "bg-blue-50 border-blue-200"}`}>
              <div className="flex items-center gap-2 mb-2">
                <div className="w-2 h-2 rounded-full bg-blue-500"></div>
                <span className={`font-medium ${isDark ? "text-blue-300" : "text-blue-800"}`}>
                  Selected: {thermalTemplates.find(t => t.id === form.thermal_template)?.name} Template
                </span>
              </div>
              <p className={`text-sm ${isDark ? "text-blue-400" : "text-blue-700"}`}>
                This template will be used for all thermal printer sales invoices.
              </p>
            </div>
          </div>
        </GlassCard>
      )}

      {/* ===== Save Button ===== */}
      <div className="flex justify-end">
        <GlassBtn
          onClick={handleSave}
          disabled={disableInputs || saving}
          className={`h-10 px-6 ${(disableInputs || saving) ? btnOutline + " opacity-60 cursor-not-allowed" : btnGreen}`}
          title={!disableInputs ? "Alt+S" : "You lack update permission"}
        >
          {saving ? "Saving…" : "Save Settings"}
        </GlassBtn>
      </div>

      {/* ===== Template Preview Modal ===== */}
      {showPreviewModal && previewingTemplate && (
        <div className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4">
          <div className={`w-full max-w-4xl max-h-[90vh] rounded-2xl shadow-2xl overflow-hidden flex flex-col ${isDark ? "bg-slate-800 border-slate-600" : "bg-white border-gray-200"} border`}>
            {/* Header */}
            <div className={`flex items-center justify-between p-4 border-b ${isDark ? "border-slate-600" : "border-gray-200"}`}>
              <div className="flex items-center gap-3">
                <div className={`p-2 rounded-lg ${isDark ? "bg-blue-900/50" : "bg-blue-100"}`}>
                  <EyeIcon className={`w-5 h-5 ${isDark ? "text-blue-400" : "text-blue-600"}`} />
                </div>
                <div>
                  <h3 className={`font-semibold ${isDark ? "text-slate-100" : "text-gray-800"}`}>{previewingTemplate.name} Template Preview</h3>
                  <p className={`text-sm ${isDark ? "text-slate-400" : "text-gray-500"}`}>{previewingTemplate.description}</p>
                </div>
              </div>
              <button
                onClick={() => {
                  setShowPreviewModal(false);
                  setPreviewingTemplate(null);
                }}
                className={`p-2 rounded-lg transition-colors ${isDark ? "hover:bg-slate-700" : "hover:bg-gray-100"}`}
              >
                <svg className={`w-5 h-5 ${isDark ? "text-slate-400" : "text-gray-500"}`} fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M6 18L18 6M6 6l12 12" />
                </svg>
              </button>
            </div>
            
            {/* Preview Content */}
            <div className={`flex-1 overflow-auto p-4 ${isDark ? "bg-slate-900" : "bg-gray-100"}`}>
              <div className={`mx-auto rounded-lg shadow-lg ${isDark ? "bg-slate-800" : "bg-white"}`} style={{ 
                maxWidth: previewingTemplate.id === 'minimal' || previewingTemplate.id === 'compact' ? '300px' : '400px',
                minHeight: '400px'
              }}>
                <iframe
                  src={`/print/thermal-preview/${previewingTemplate.id}`}
                  className="w-full h-full border-0"
                  style={{ minHeight: '400px', width: previewingTemplate.id === 'minimal' || previewingTemplate.id === 'compact' ? '280px' : '380px' }}
                  title={`${previewingTemplate.name} Template Preview`}
                />
              </div>
            </div>
            
            {/* Footer */}
            <div className={`flex items-center justify-between p-4 border-t ${isDark ? "border-slate-600 bg-slate-800" : "border-gray-200 bg-gray-50"}`}>
              <a
                href={`/print/thermal-preview/${previewingTemplate.id}`}
                target="_blank"
                rel="noopener noreferrer"
                className={`px-4 py-2 rounded-lg transition-colors flex items-center gap-2 ${isDark ? "hover:bg-slate-700 text-slate-300" : "hover:bg-gray-200 text-gray-600"}`}
              >
                <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M10 6H6a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-4M14 4h6m0 0v6m0-6L10 14" />
                </svg>
                Open in New Tab
              </a>
              <div className="flex gap-2">
                <GlassBtn
                  onClick={() => {
                    setShowPreviewModal(false);
                    setPreviewingTemplate(null);
                  }}
                  className={`h-9 px-4 ${btnOutline}`}
                >
                  Close
                </GlassBtn>
                <GlassBtn
                  onClick={() => {
                    if (!disableInputs) {
                      handleTemplateSelect(previewingTemplate.id);
                      setShowPreviewModal(false);
                      setPreviewingTemplate(null);
                      toast.success(`Selected ${previewingTemplate.name} template`);
                    } else {
                      toast.error("You don't have permission to update settings.");
                    }
                  }}
                  disabled={disableInputs}
                  className={`h-9 px-4 ${btnBlue}`}
                >
                  {form.thermal_template === previewingTemplate.id 
                    ? "Already Selected" 
                    : `Select ${previewingTemplate.name}`}
                </GlassBtn>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

