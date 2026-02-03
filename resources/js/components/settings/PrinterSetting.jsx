import { useState } from "react";
import toast from "react-hot-toast";
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
import { GlassCard, GlassSectionHeader, GlassToolbar, GlassInput, GlassBtn } from "@/components/glass.jsx";

export default function PrinterSetting({ 
  form, 
  handleChange, 
  disableInputs, 
  saving,
  handleSave,
  tintBlue,
  tintGlass,
  tintGreen
}) {
  // Thermal template selection
  const [selectedThermalTemplate, setSelectedThermalTemplate] = useState(form.thermal_template || "standard");
  
  // Preview modal state
  const [showPreviewModal, setShowPreviewModal] = useState(false);
  const [previewingTemplate, setPreviewingTemplate] = useState(null);

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
    <>
      {/* ===== Invoice Footer Note (Moved from General tab) ===== */}
      <GlassCard>
        <GlassSectionHeader title="Invoice Footer Note" />
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
            className="w-full h-24 rounded-xl bg-white/70 backdrop-blur-sm border border-gray-200/70 ring-1 ring-transparent focus:ring-blue-400/40 shadow-sm focus:outline-none px-3 py-2
              dark:bg-slate-700/70 dark:border-slate-600/70 dark:text-gray-100 dark:placeholder-gray-400 dark:focus:ring-indigo-400/40"
            placeholder="This note will be printed at the bottom of the invoice…"
          />
          <p className="text-xs text-gray-500 mt-2 dark:text-gray-400">
            This note will appear at the bottom of all printed invoices and receipts.
          </p>
        </div>
      </GlassCard>

      {/* ===== Printer Type Selection ===== */}
      <GlassCard>
        <GlassSectionHeader title="Printer Type" />
        <GlassToolbar className="flex flex-wrap gap-4">
          <label className={`inline-flex items-center gap-3 px-4 py-3 rounded-xl ring-1 ring-gray-200/70 cursor-pointer transition-all ${
            form.printer_type === "thermal" ? "bg-blue-50 ring-blue-300 dark:bg-blue-900/30 dark:ring-blue-700" : "bg-white/70 hover:bg-white/90 dark:bg-slate-700/60 dark:ring-slate-600/60 dark:hover:bg-slate-600/60"
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
              <PrinterIcon className="w-5 h-5 text-gray-600 dark:text-gray-300" />
              <span className="text-sm font-medium dark:text-gray-200">Thermal Printer</span>
            </div>
          </label>
          
          <label className={`inline-flex items-center gap-3 px-4 py-3 rounded-xl ring-1 ring-gray-200/70 cursor-pointer transition-all ${
            form.printer_type === "a4" ? "bg-blue-50 ring-blue-300 dark:bg-blue-900/30 dark:ring-blue-700" : "bg-white/70 hover:bg-white/90 dark:bg-slate-700/60 dark:ring-slate-600/60 dark:hover:bg-slate-600/60"
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
              <DocumentTextIcon className="w-5 h-5 text-gray-600 dark:text-gray-300" />
              <span className="text-sm font-medium dark:text-gray-200">A4 Printer</span>
            </div>
          </label>
        </GlassToolbar>
        <div className="px-4 pb-4">
          <p className="text-xs text-gray-500 dark:text-gray-400">
            {form.printer_type === "thermal" 
              ? "Select a thermal receipt template below. Thermal printers use 58mm-80mm width paper."
              : "A4 printer settings will be available in a future update."}
          </p>
        </div>
      </GlassCard>

      {/* ===== Thermal Template Selection ===== */}
      {form.printer_type === "thermal" && (
        <GlassCard>
          <GlassSectionHeader 
            title="Thermal Receipt Template"
            subtitle="Choose how your receipts will look when printed on thermal printers"
          />
          
          {/* Template Grid */}
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4 p-4">
            {thermalTemplates.map((template) => {
              const IconComponent = getIconComponent(template.icon);
              const isSelected = selectedThermalTemplate === template.id;
              
              return (
                <div
                  key={template.id}
                  className={`relative rounded-xl border-2 transition-all cursor-pointer overflow-hidden group ${
                    isSelected
                      ? "border-blue-500 ring-2 ring-blue-200 dark:ring-blue-800"
                      : "border-gray-200 hover:border-gray-300 hover:shadow-md dark:border-slate-600 dark:hover:border-slate-500 dark:bg-slate-800/50"
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
                    className="absolute top-2 left-2 z-10 p-1.5 rounded-lg bg-white/90 hover:bg-white shadow-sm opacity-100 transition-opacity dark:bg-slate-700/90 dark:hover:bg-slate-600"
                    title="Preview template"
                  >
                    <EyeIcon className="w-4 h-4 text-gray-600 dark:text-gray-300" />
                  </button>
                  
                  {/* Template Content */}
                  <div className="p-4">
                    {/* Small Thumbnail Preview */}
                    <div className="mb-3 bg-white rounded-lg border border-gray-200 overflow-hidden dark:bg-slate-800 dark:border-slate-600">
                      <iframe
                        src={`/print/thermal-preview/${template.id}`}
                        className="w-full h-24 border-0"
                        style={{ 
                          transform: 'scale(0.5)',
                          transformOrigin: 'top left',
                          width: '200%',
                          height: '200%'
                        }}
                        title={`${template.name} Thumbnail`}
                      />
                    </div>
                    
                    {/* Icon and Name */}
                    <div className="flex items-start gap-3 mb-3">
                      <div className={`p-2 rounded-lg ${isSelected ? "bg-blue-100 dark:bg-blue-900/50" : "bg-gray-100 dark:bg-slate-700"}`}>
                        <IconComponent className={`w-6 h-6 ${isSelected ? "text-blue-600" : "text-gray-600 dark:text-gray-300"}`} />
                      </div>
                      <div className="flex-1">
                        <h4 className="font-semibold text-gray-800 dark:text-gray-100">{template.name}</h4>
                        <p className="text-xs text-gray-500 mt-1 dark:text-gray-400">{template.description}</p>
                      </div>
                    </div>
                    
                    {/* Preview Info */}
                    <div className="bg-gray-50 rounded-lg p-2 mb-3 dark:bg-slate-700/50">
                      <p className="text-xs text-gray-600 dark:text-gray-300">{template.preview}</p>
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
                          : "bg-gray-100 text-gray-700 hover:bg-gray-200 dark:bg-slate-700 dark:text-gray-200 dark:hover:bg-slate-600"
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
            <div className="bg-blue-50 rounded-xl p-4 border border-blue-200 dark:bg-blue-900/30 dark:border-blue-800">
              <div className="flex items-center gap-2 mb-2">
                <div className="w-2 h-2 rounded-full bg-blue-500"></div>
                <span className="font-medium text-blue-800 dark:text-blue-300">
                  Selected: {thermalTemplates.find(t => t.id === form.thermal_template)?.name} Template
                </span>
              </div>
              <p className="text-sm text-blue-700 dark:text-blue-400">
                This template will be used for all thermal printer sales invoices.
              </p>
            </div>
          </div>
        </GlassCard>
      )}

      {/* ===== Save Button for Printer Settings ===== */}
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

      {/* ===== Template Preview Modal ===== */}
      {showPreviewModal && previewingTemplate && (
        <div className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4">
          <div className="bg-white rounded-2xl shadow-2xl w-full max-w-4xl max-h-[90vh] overflow-hidden flex flex-col">
            {/* Header */}
            <div className="flex items-center justify-between p-4 border-b border-gray-200">
              <div className="flex items-center gap-3">
                <div className="p-2 rounded-lg bg-blue-100">
                  <EyeIcon className="w-5 h-5 text-blue-600" />
                </div>
                <div>
                  <h3 className="font-semibold text-gray-800">{previewingTemplate.name} Template Preview</h3>
                  <p className="text-sm text-gray-500">{previewingTemplate.description}</p>
                </div>
              </div>
              <button
                onClick={() => {
                  setShowPreviewModal(false);
                  setPreviewingTemplate(null);
                }}
                className="p-2 rounded-lg hover:bg-gray-100 transition-colors"
              >
                <svg className="w-5 h-5 text-gray-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M6 18L18 6M6 6l12 12" />
                </svg>
              </button>
            </div>
            
            {/* Preview Content - Actual Template Preview */}
            <div className="flex-1 overflow-auto bg-gray-100 p-4">
              <div className="bg-white rounded-lg shadow-lg mx-auto" style={{ 
                maxWidth: previewingTemplate.id === 'minimal' || previewingTemplate.id === 'compact' ? '300px' : '400px',
                minHeight: '400px'
              }}>
                <iframe
                  src={`/print/thermal-preview/${previewingTemplate.id}`}
                  className="w-full h-full border-0"
                  style={{ 
                    minHeight: '400px',
                    width: previewingTemplate.id === 'minimal' || previewingTemplate.id === 'compact' ? '280px' : '380px'
                  }}
                  title={`${previewingTemplate.name} Template Preview`}
                />
              </div>
              
              {/* Template Features */}
              <div className="mt-4 bg-white rounded-lg p-4 mx-auto" style={{ maxWidth: '400px' }}>
                <h4 className="font-medium text-gray-800 mb-3">Template Features</h4>
                <ul className="space-y-2 text-sm text-gray-600">
                  <li className="flex items-center gap-2">
                    <svg className="w-4 h-4 text-green-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M5 13l4 4L19 7" />
                    </svg>
                    {previewingTemplate.preview}
                  </li>
                  <li className="flex items-center gap-2">
                    <svg className="w-4 h-4 text-green-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M5 13l4 4L19 7" />
                    </svg>
                    Optimized for thermal printer width (58mm-80mm)
                  </li>
                  <li className="flex items-center gap-2">
                    <svg className="w-4 h-4 text-green-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M5 13l4 4L19 7" />
                    </svg>
                    Print-ready format with proper page sizing
                  </li>
                </ul>
              </div>
            </div>
            
            {/* Footer */}
            <div className="flex justify-between items-center p-4 border-t border-gray-200 bg-gray-50">
              <button
                onClick={() => {
                  setShowPreviewModal(false);
                  setPreviewingTemplate(null);
                }}
                className="px-4 py-2 rounded-lg text-gray-600 hover:bg-gray-200 transition-colors"
              >
                Close
              </button>
              <div className="flex gap-2">
                <a
                  href={`/print/thermal-preview/${previewingTemplate.id}`}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="px-4 py-2 rounded-lg text-gray-600 hover:bg-gray-200 transition-colors flex items-center gap-2"
                >
                  <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M10 6H6a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-4M14 4h6m0 0v6m0-6L10 14" />
                  </svg>
                  Open in New Tab
                </a>
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
                  className={`h-9 px-4 ${tintBlue}`}
                >
                  {form.thermal_template === previewingTemplate.id 
                    ? "Already Selected" 
                    : `Select ${previewingTemplate.name}`
                  }
                </GlassBtn>
              </div>
            </div>
          </div>
        </div>
      )}
    </>
  );
}

