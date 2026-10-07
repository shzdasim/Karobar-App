// resources/js/components/settings/PrinterSetting.jsx
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
  QrCodeIcon,
} from "@heroicons/react/24/solid";

export default function PrinterSetting({
  form,
  handleChange,
  disableInputs,
  saving,
  handleSave,
}) {
  const [selectedThermalTemplate, setSelectedThermalTemplate] = useState(form.thermal_template || "standard");
  const [selectedA4Template, setSelectedA4Template] = useState(form.a4_template || "standard");
  const [showPreviewModal, setShowPreviewModal] = useState(false);
  const [previewingTemplate, setPreviewingTemplate] = useState(null);
  const [previewingType, setPreviewingType] = useState('thermal'); // 'thermal' or 'a4'

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

  // A4 templates data - same as thermal templates
  const a4Templates = [
    {
      id: 'standard',
      name: 'Standard',
      description: 'Classic layout with logo support',
      icon: 'DocumentTextIcon',
      preview: 'Standard A4 layout with store branding'
    },
    {
      id: 'minimal',
      name: 'Minimal',
      description: 'Clean and simple design',
      icon: 'DocumentIcon',
      preview: 'Minimal A4 receipt'
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
      description: 'Space-efficient layout',
      icon: 'ScaleIcon',
      preview: 'Compact A4 format'
    },
    {
      id: 'bold',
      name: 'Bold',
      description: 'Large fonts, high emphasis',
      icon: 'BoltIcon',
      preview: 'Bold fonts with high contrast'
    },
    {
      id: 'barcode',
      name: 'Barcode',
      description: 'With barcodes & QR code',
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

  const handleA4TemplateSelect = (templateId) => {
    if (!disableInputs) {
      setSelectedA4Template(templateId);
      handleChange({ target: { name: 'a4_template', value: templateId } });
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

  const isThermal = form.printer_type === "thermal";

  const renderTemplateGrid = ({ templates, selectedId, onSelect, type }) => (
    <div className="settings-template-grid">
      {templates.map((template) => {
        const IconComponent = getIconComponent(template.icon);
        const isSelected = selectedId === template.id;

        return (
          <div
            key={template.id}
            role="button"
            tabIndex={0}
            aria-pressed={isSelected}
            className={`settings-template ${isSelected ? "is-active" : ""}`}
            onClick={() => onSelect(template.id)}
            onKeyDown={(e) => {
              if (e.key === "Enter" || e.key === " ") {
                e.preventDefault();
                onSelect(template.id);
              }
            }}
          >
            {/* Selection indicator */}
            {isSelected && (
              <span className="settings-template-badge" aria-hidden="true">
                <svg fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="3" d="M5 13l4 4L19 7" />
                </svg>
              </span>
            )}

            {/* Preview trigger */}
            <button
              type="button"
              onClick={(e) => {
                e.stopPropagation();
                setPreviewingTemplate(template);
                setPreviewingType(type);
                setShowPreviewModal(true);
              }}
              className="settings-template-preview-btn"
              title="Preview template"
              aria-label={`Preview ${template.name} template`}
            >
              <EyeIcon aria-hidden="true" />
            </button>

            {/* Thumbnail */}
            <span className="settings-template-preview">
              <iframe
                src={`/print/${type}-preview/${template.id}`}
                className="border-0"
                title={`${template.name} Thumbnail`}
              />
            </span>

            {/* Name + description */}
            <span className="settings-template-head">
              <span className="settings-template-icon">
                <IconComponent aria-hidden="true" />
              </span>
              <span className="min-w-0">
                <span className="settings-option-title block">{template.name}</span>
                <span className="settings-option-tagline block">{template.description}</span>
              </span>
            </span>

            <span className="settings-template-note">{template.preview}</span>

            {/* Select action */}
            <button
              type="button"
              onClick={(e) => {
                e.stopPropagation();
                if (!disableInputs) {
                  onSelect(template.id);
                } else {
                  toast.error("You don't have permission to update settings.");
                }
              }}
              disabled={disableInputs}
              className={`products-action w-full ${isSelected ? "products-action-primary" : ""}`}
            >
              {isSelected ? "Selected" : "Select template"}
            </button>
          </div>
        );
      })}
    </div>
  );

  return (
    <>
      {/* ===== Invoice footer note ===== */}
      <div className="settings-block">
        <div className="settings-block-heading">
          <div className="flex items-center gap-3 min-w-0">
            <div className="settings-block-icon">
              <DocumentTextIcon />
            </div>
            <div className="min-w-0">
              <h2 className="settings-block-title">Invoice Footer Note</h2>
              <p className="settings-block-note">Custom message at the bottom of invoices</p>
            </div>
          </div>
        </div>

        <div className="settings-body">
          <div className="settings-field">
            <label className="settings-label" htmlFor="setting-invoice-note">Footer note</label>
            <textarea
              id="setting-invoice-note"
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
              placeholder="This note will be printed at the bottom of the invoice..."
            />
            <p className="settings-hint">
              This note will appear at the bottom of all printed invoices and receipts. Ctrl/⌘ + Enter saves.
            </p>
          </div>
        </div>
      </div>

      {/* ===== Printer type ===== */}
      <div className="settings-block">
        <div className="settings-block-heading">
          <div className="flex items-center gap-3 min-w-0">
            <div className="settings-block-icon">
              <PrinterIcon />
            </div>
            <div className="min-w-0">
              <h2 className="settings-block-title">Printer Type</h2>
              <p className="settings-block-note">Select your printer type</p>
            </div>
          </div>
        </div>

        <div className="settings-body">
          <div className="settings-options settings-options-2">
            <label className={`settings-option ${isThermal ? "is-active" : ""}`}>
              <input
                type="radio"
                name="printer_type"
                value="thermal"
                checked={isThermal}
                onChange={handleChange}
                disabled={disableInputs}
                className="sr-only"
              />
              <span className="settings-option-head">
                <PrinterIcon aria-hidden="true" />
                <span className="settings-option-title">Thermal Printer</span>
              </span>
              <span className="settings-option-tagline">
                58mm–80mm receipt paper with a compact thermal template.
              </span>
            </label>

            <label className={`settings-option ${!isThermal ? "is-active" : ""}`}>
              <input
                type="radio"
                name="printer_type"
                value="a4"
                checked={!isThermal}
                onChange={handleChange}
                disabled={disableInputs}
                className="sr-only"
              />
              <span className="settings-option-head">
                <DocumentTextIcon aria-hidden="true" />
                <span className="settings-option-title">A4 Printer</span>
              </span>
              <span className="settings-option-tagline">
                Standard letter-size paper with a full invoice template.
              </span>
            </label>
          </div>

          <p className="settings-hint">
            {isThermal
              ? "A thermal receipt template is used for every thermal invoice."
              : "An A4 invoice template is used for every A4 invoice."}
          </p>
        </div>
      </div>

      {/* ===== Template selection ===== */}
      <div className="settings-block">
        <div className="settings-block-heading">
          <div className="flex items-center gap-3 min-w-0">
            <div className="settings-block-icon">
              <DocumentTextIcon />
            </div>
            <div className="min-w-0">
              <h2 className="settings-block-title">
                {isThermal ? "Thermal Receipt Template" : "A4 Invoice Template"}
              </h2>
              <p className="settings-block-note">
                {isThermal ? "Choose your receipt layout" : "Choose your invoice layout"}
              </p>
            </div>
          </div>
        </div>

        <div className="settings-body">
          {isThermal
            ? renderTemplateGrid({
                templates: thermalTemplates,
                selectedId: selectedThermalTemplate,
                onSelect: handleTemplateSelect,
                type: 'thermal',
              })
            : renderTemplateGrid({
                templates: a4Templates,
                selectedId: selectedA4Template,
                onSelect: handleA4TemplateSelect,
                type: 'a4',
              })}

          <div className="settings-note">
            <DocumentTextIcon aria-hidden="true" />
            <div>
              <span className="settings-note-title">
                Selected: {isThermal
                  ? thermalTemplates.find(t => t.id === form.thermal_template)?.name
                  : a4Templates.find(t => t.id === form.a4_template)?.name} Template
              </span>
              This template will be used for all {isThermal ? "thermal printer" : "A4 printer"} sales invoices.
            </div>
          </div>
        </div>
      </div>

      {/* ===== Save ===== */}
      <div className="settings-block">
        <div className="settings-body">
          <div className="settings-actions settings-actions-between">
            <span className="settings-hint">
              Shortcut: <span className="people-shortcut">Alt+S</span> to save
            </span>
            <button
              type="button"
              onClick={handleSave}
              disabled={disableInputs || saving}
              className="products-action products-action-primary"
              title={!disableInputs ? "Alt+S" : "You lack update permission"}
            >
              {saving ? "Saving…" : "Save settings"}
            </button>
          </div>
        </div>
      </div>

      {/* ===== Template preview modal ===== */}
      {showPreviewModal && previewingTemplate && (
        <div
          className="settings-modal"
          role="dialog"
          aria-modal="true"
          aria-labelledby="template-preview-title"
          onClick={(e) => {
            if (e.target === e.currentTarget) {
              setShowPreviewModal(false);
              setPreviewingTemplate(null);
            }
          }}
        >
          <div className="settings-modal-card settings-modal-card-lg">
            {/* Header */}
            <div className="settings-modal-heading">
              <div className="settings-block-icon">
                <EyeIcon />
              </div>
              <div className="min-w-0">
                <h3 id="template-preview-title" className="settings-block-title">
                  {previewingTemplate.name} Template Preview
                </h3>
                <p className="settings-block-note">{previewingTemplate.description}</p>
              </div>
              <button
                type="button"
                onClick={() => {
                  setShowPreviewModal(false);
                  setPreviewingTemplate(null);
                }}
                className="products-action"
                style={{ marginLeft: "auto" }}
                aria-label="Close preview"
              >
                <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M6 18L18 6M6 6l12 12" />
                </svg>
              </button>
            </div>

            {/* Preview */}
            <div className="settings-modal-body-flush">
              <div
                className="settings-template-frame"
                style={{
                  maxWidth: previewingTemplate.id === 'minimal' || previewingTemplate.id === 'compact' ? '300px' : '400px',
                }}
              >
                <iframe
                  src={`/print/${previewingType}-preview/${previewingTemplate.id}`}
                  className="border-0"
                  style={{ minHeight: '400px', width: '100%' }}
                  title={`${previewingTemplate.name} Template Preview`}
                />
              </div>
            </div>

            {/* Footer */}
            <div className="settings-modal-footer">
              <a
                href={`/print/${previewingType}-preview/${previewingTemplate.id}`}
                target="_blank"
                rel="noopener noreferrer"
                className="products-action"
              >
                <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M10 6H6a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-4M14 4h6m0 0v6m0-6L10 14" />
                </svg>
                <span>Open in new tab</span>
              </a>

              <div className="flex flex-wrap gap-2">
                <button
                  type="button"
                  onClick={() => {
                    setShowPreviewModal(false);
                    setPreviewingTemplate(null);
                  }}
                  className="products-action"
                >
                  Close
                </button>
                <button
                  type="button"
                  onClick={() => {
                    if (!disableInputs) {
                      if (previewingType === 'a4') {
                        handleA4TemplateSelect(previewingTemplate.id);
                      } else {
                        handleTemplateSelect(previewingTemplate.id);
                      }
                      setShowPreviewModal(false);
                      setPreviewingTemplate(null);
                      toast.success(`Selected ${previewingTemplate.name} ${previewingType === 'a4' ? 'A4' : 'thermal'} template`);
                    } else {
                      toast.error("You don't have permission to update settings.");
                    }
                  }}
                  disabled={disableInputs}
                  className="products-action products-action-primary"
                >
                  {(previewingType === 'a4' ? form.a4_template : form.thermal_template) === previewingTemplate.id
                    ? "Already selected"
                    : `Select ${previewingTemplate.name}`}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
