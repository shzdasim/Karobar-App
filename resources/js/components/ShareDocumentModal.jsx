import React, { useState, useEffect, useMemo } from "react";
import axios from "axios";
import toast from "react-hot-toast";
import {
  ShareIcon,
  EnvelopeIcon,
  ChatBubbleLeftRightIcon,
  DocumentArrowDownIcon,
  DocumentTextIcon,
  EyeIcon,
  ClipboardDocumentCheckIcon,
  XMarkIcon,
  PaperAirplaneIcon,
  ArrowPathIcon,
  CheckCircleIcon,
} from "@heroicons/react/24/outline";
import { useTheme } from "@/context/ThemeContext";
import { getThemeButtonClasses } from "@/utils/buttonStyles";

/**
 * Normalize a phone number on the client side for wa.me links (mirrors DocumentShareService::normalizePhone).
 */
function normalizeWhatsAppPhone(rawPhone, defaultCountryCode = "92") {
  if (!rawPhone) return "";
  let digits = String(rawPhone).replace(/\D+/g, "");
  if (!digits) return "";
  if (digits.startsWith("00")) {
    digits = digits.slice(2);
  }
  if (digits.length === 11 && digits.startsWith("03")) {
    return defaultCountryCode + digits.slice(1);
  }
  if (digits.length === 10 && digits.startsWith("3")) {
    return defaultCountryCode + digits;
  }
  if (digits.startsWith("0") && digits.length >= 10 && digits.length <= 11) {
    return defaultCountryCode + digits.slice(1);
  }
  return digits;
}

function base64ToPdfFile(base64, filename) {
  const byteCharacters = atob(base64);
  const byteNumbers = new Array(byteCharacters.length);
  for (let i = 0; i < byteCharacters.length; i++) {
    byteNumbers[i] = byteCharacters.charCodeAt(i);
  }
  const byteArray = new Uint8Array(byteNumbers);
  const blob = new Blob([byteArray], { type: "application/pdf" });
  const file = new File([blob], filename, { type: "application/pdf" });
  return { blob, file };
}

export default function ShareDocumentModal({
  isOpen,
  onClose,
  documentType, // 'sale_invoice' | 'customer_ledger' | 'supplier_ledger' | 'payment' | 'purchase_order'
  documentId = null,
  queryParams = {}, // e.g. { customer_id, supplier_id, from, to }
  initialTab = "whatsapp", // 'whatsapp' | 'email'
}) {
  const { isDark, theme } = useTheme();

  const themeColors = useMemo(() => {
    if (!theme) {
      return {
        primary: "#3b82f6",
        primaryHover: "#2563eb",
        primaryLight: "#dbeafe",
        secondary: "#8b5cf6",
        secondaryHover: "#7c3aed",
        secondaryLight: "#ede9fe",
      };
    }
    return {
      primary: theme.primary_color || "#3b82f6",
      primaryHover: theme.primary_hover || "#2563eb",
      primaryLight: theme.primary_light || "#dbeafe",
      secondary: theme.secondary_color || "#8b5cf6",
      secondaryHover: theme.secondary_hover || "#7c3aed",
      secondaryLight: theme.secondary_light || "#ede9fe",
    };
  }, [theme]);

  const buttonStyle = theme?.button_style || "modern";
  const btnClasses = useMemo(
    () => getThemeButtonClasses(buttonStyle, themeColors, isDark),
    [buttonStyle, themeColors, isDark]
  );
  const btnPrimary = btnClasses.primary;
  const btnSecondary = btnClasses.secondary;

  const [activeTab, setActiveTab] = useState(initialTab);
  const [loading, setLoading] = useState(false);
  const [buildingPdf, setBuildingPdf] = useState(false);
  const [meta, setMeta] = useState(null);

  // Built PDF file state
  const [pdfFile, setPdfFile] = useState(null);
  const [pdfBlobUrl, setPdfBlobUrl] = useState(null);
  const [showInlinePreview, setShowInlinePreview] = useState(false);

  // WhatsApp form state
  const [phoneInput, setPhoneInput] = useState("");
  const [whatsappMessage, setWhatsappMessage] = useState("");
  const [sendingWhatsApp, setSendingWhatsApp] = useState(false);

  // Email form state
  const [emailInput, setEmailInput] = useState("");
  const [emailSubject, setEmailSubject] = useState("");
  const [emailBody, setEmailBody] = useState("");
  const [saveEmailToProfile, setSaveEmailToProfile] = useState(true);
  const [sendingEmail, setSendingEmail] = useState(false);

  const buildRequestParams = () => ({
    type: documentType,
    ...(documentId ? { id: documentId } : {}),
    ...queryParams,
  });

  useEffect(() => {
    if (isOpen && documentType) {
      setActiveTab(initialTab || "whatsapp");
      setShowInlinePreview(false);
      loadMetadataAndBuildPdf();
    }
    return () => {
      if (pdfBlobUrl) {
        window.URL.revokeObjectURL(pdfBlobUrl);
      }
    };
  }, [isOpen, documentType, documentId, JSON.stringify(queryParams)]);

  const loadMetadataAndBuildPdf = async () => {
    setLoading(true);
    setBuildingPdf(true);
    try {
      const params = buildRequestParams();
      const [metaRes, pdfRes] = await Promise.all([
        axios.get("/api/share/metadata", { params }),
        axios.get("/api/share/pdf", { params, responseType: "blob" }),
      ]);

      const data = metaRes.data;
      setMeta(data);
      setPhoneInput(data.recipient_phone || data.normalized_phone || "");
      setWhatsappMessage(data.whatsapp_text || "");
      setEmailInput(data.recipient_email || "");
      setEmailSubject(data.email_subject || "");
      setEmailBody(data.email_body || "");

      const filename = data.filename || `${documentType}_document.pdf`;
      const blob = new Blob([pdfRes.data], { type: "application/pdf" });
      const file = new File([blob], filename, { type: "application/pdf" });

      if (pdfBlobUrl) {
        window.URL.revokeObjectURL(pdfBlobUrl);
      }
      const nextUrl = window.URL.createObjectURL(blob);
      setPdfFile(file);
      setPdfBlobUrl(nextUrl);
    } catch (err) {
      toast.error(
        err?.response?.data?.message || "Failed to build PDF document for sharing"
      );
    } finally {
      setLoading(false);
      setBuildingPdf(false);
    }
  };

  if (!isOpen) return null;

  const normalizedPhone = normalizeWhatsAppPhone(phoneInput);
  const canUseNativeFileShare =
    typeof navigator !== "undefined" &&
    typeof navigator.canShare === "function" &&
    pdfFile &&
    navigator.canShare({ files: [pdfFile] });

  const triggerBrowserDownload = (fileOrBlob, filename) => {
    const url = window.URL.createObjectURL(fileOrBlob);
    const a = document.createElement("a");
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => window.URL.revokeObjectURL(url), 5000);
  };

  /**
   * Build the actual PDF file via backend and send it on WhatsApp (no URL link in text).
   */
  const handleSendWhatsAppPdf = async () => {
    setSendingWhatsApp(true);
    try {
      const payload = {
        ...buildRequestParams(),
        phone: phoneInput.trim(),
        message: whatsappMessage.trim() || meta?.document_title || "Attached PDF Document",
      };

      const { data } = await axios.post("/api/share/whatsapp", payload);

      // 1. If server-side WhatsApp Cloud API sent the actual PDF document directly:
      if (data.mode === "cloud_api") {
        toast.success(data.message || "PDF document sent directly to WhatsApp!");
        onClose?.();
        return;
      }

      // 2. Reconstruct the freshly built PDF file from the backend response
      let currentFile = pdfFile;
      const filename = data.filename || meta?.filename || `${documentType}.pdf`;
      if (data.pdf_base64) {
        const built = base64ToPdfFile(data.pdf_base64, filename);
        currentFile = built.file;
        setPdfFile(built.file);
      }

      // 3. Download the actual built PDF file so the user has the PDF file ready
      if (currentFile) {
        triggerBrowserDownload(currentFile, filename);
      }

      // 4. Open WhatsApp chat for the target phone number with the clean caption (no link!)
      const targetPhone = data.normalized_phone || normalizedPhone;
      const encodedCaption = encodeURIComponent(whatsappMessage.trim());
      const waUrl = targetPhone
        ? `https://wa.me/${targetPhone}?text=${encodedCaption}`
        : `https://wa.me/?text=${encodedCaption}`;

      window.open(waUrl, "_blank", "noopener,noreferrer");
      toast.success(`Built ${filename} — attach the downloaded PDF in WhatsApp`);
    } catch (err) {
      toast.error(
        err?.response?.data?.message || "Failed to build & send PDF for WhatsApp"
      );
    } finally {
      setSendingWhatsApp(false);
    }
  };

  /**
   * Share the actual PDF File object directly via the OS Share Sheet (macOS / iOS / Android / Windows).
   */
  const handleNativeOsFileShare = async () => {
    if (!pdfFile) return;
    try {
      await navigator.share({
        files: [pdfFile],
        title: meta?.document_title || pdfFile.name,
        text: whatsappMessage.trim(),
      });
      toast.success("Shared actual PDF file");
    } catch (err) {
      if (err?.name !== "AbortError") {
        toast.error("Could not invoke native file share");
      }
    }
  };

  const handleCopyWhatsAppMessage = async () => {
    try {
      await navigator.clipboard.writeText(whatsappMessage);
      toast.success("Caption copied to clipboard");
    } catch {
      toast.error("Could not copy message");
    }
  };

  const handleSendEmail = async (e) => {
    e.preventDefault();
    if (!emailInput.trim()) {
      toast.error("Please enter a recipient email address");
      return;
    }
    if (!emailSubject.trim()) {
      toast.error("Please enter an email subject");
      return;
    }

    setSendingEmail(true);
    try {
      const payload = {
        ...buildRequestParams(),
        email: emailInput.trim(),
        subject: emailSubject.trim(),
        message: emailBody,
        save_email: Boolean(saveEmailToProfile),
      };
      const { data } = await axios.post("/api/share/email", payload);
      if (data.delivery_mode === "log_fallback") {
        const filename = data.filename || meta?.filename || `${documentType}.pdf`;
        if (data.pdf_base64) {
          const built = base64ToPdfFile(data.pdf_base64, filename);
          triggerBrowserDownload(built.file, filename);
        } else if (pdfFile) {
          triggerBrowserDownload(pdfFile, filename);
        }
      }
      toast.success(data.message || "Email sent with PDF attachment!");
      onClose?.();
    } catch (err) {
      toast.error(
        err?.response?.data?.message || "Failed to send email. Check mail settings."
      );
    } finally {
      setSendingEmail(false);
    }
  };

  const handleDownloadBuiltPdf = () => {
    if (!pdfFile) return;
    triggerBrowserDownload(pdfFile, pdfFile.name);
    toast.success(`Downloaded ${pdfFile.name}`);
  };

  const inputClass = `w-full rounded-lg border px-3.5 py-2 text-sm transition-colors focus:outline-none focus:ring-2 ${
    isDark
      ? "bg-slate-900 border-slate-700 text-slate-100 placeholder-slate-500 focus:border-blue-500 focus:ring-blue-500/20"
      : "bg-white border-gray-300 text-gray-900 placeholder-gray-400 focus:border-blue-500 focus:ring-blue-500/20"
  }`;

  const pdfSizeKb = pdfFile ? (pdfFile.size / 1024).toFixed(1) : null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm p-4">
      <div
        className={`w-full max-w-2xl rounded-2xl border shadow-2xl overflow-hidden transition-all ${
          isDark
            ? "bg-slate-800 border-slate-700 text-slate-100"
            : "bg-white border-gray-200 text-gray-900"
        }`}
      >
        {/* Modal Header */}
        <div
          className={`flex items-center justify-between px-6 py-4 border-b ${
            isDark ? "border-slate-700 bg-slate-900/50" : "border-gray-200 bg-gray-50"
          }`}
        >
          <div className="flex items-center gap-3">
            <div
              className="p-2.5 rounded-xl"
              style={{
                backgroundColor: isDark ? "rgba(59,130,246,0.15)" : themeColors.primaryLight,
                color: themeColors.primary,
              }}
            >
              <ShareIcon className="w-5 h-5" />
            </div>
            <div>
              <h2 className="text-lg font-bold">
                {meta?.document_title ? `Send PDF — ${meta.document_title}` : "Build & Send PDF Document"}
              </h2>
              <p className={`text-xs ${isDark ? "text-slate-400" : "text-gray-500"}`}>
                {meta?.recipient_name
                  ? `Recipient: ${meta.recipient_name} • Actual A4 PDF generated`
                  : "Generates an A4 PDF document to send via WhatsApp or Email"}
              </p>
            </div>
          </div>
          <button
            type="button"
            onClick={onClose}
            className={`p-2 rounded-lg transition-colors ${
              isDark
                ? "text-slate-400 hover:text-slate-200 hover:bg-slate-700"
                : "text-gray-500 hover:text-gray-800 hover:bg-gray-200"
            }`}
            aria-label="Close modal"
          >
            <XMarkIcon className="w-5 h-5" />
          </button>
        </div>

        {/* Built PDF Status Banner */}
        <div
          className={`px-6 py-3 border-b flex flex-wrap items-center justify-between gap-2 ${
            isDark ? "bg-slate-900/80 border-slate-700" : "bg-blue-50/70 border-blue-100"
          }`}
        >
          <div className="flex items-center gap-2.5">
            {buildingPdf ? (
              <ArrowPathIcon className="w-5 h-5 text-blue-500 animate-spin" />
            ) : (
              <CheckCircleIcon className="w-5 h-5 text-emerald-500" />
            )}
            <div className="text-xs">
              <span className="font-semibold">
                {buildingPdf
                  ? "Building PDF Document..."
                  : pdfFile
                  ? `Built PDF: ${pdfFile.name}`
                  : "PDF Ready"}
              </span>
              {pdfSizeKb && !buildingPdf && (
                <span className={`ml-2 ${isDark ? "text-slate-400" : "text-gray-500"}`}>
                  ({pdfSizeKb} KB)
                </span>
              )}
            </div>
          </div>

          {pdfBlobUrl && !buildingPdf && (
            <button
              type="button"
              onClick={() => setShowInlinePreview((v) => !v)}
              className="text-xs font-semibold text-blue-600 dark:text-blue-400 hover:underline flex items-center gap-1"
            >
              <EyeIcon className="w-4 h-4" />
              <span>{showInlinePreview ? "Hide PDF Preview" : "View Built PDF"}</span>
            </button>
          )}
        </div>

        {/* Optional Inline PDF Viewer */}
        {showInlinePreview && pdfBlobUrl && (
          <div className="px-6 pt-4">
            <iframe
              src={pdfBlobUrl}
              title="Built PDF Preview"
              className="w-full h-64 rounded-lg border border-gray-300 dark:border-slate-700 bg-white"
            />
          </div>
        )}

        {/* Mode Selector Tabs */}
        <div
          className={`grid grid-cols-2 gap-2 px-6 pt-4 pb-2 border-b ${
            isDark ? "border-slate-700" : "border-gray-200"
          }`}
        >
          <button
            type="button"
            onClick={() => setActiveTab("whatsapp")}
            className={`px-4 py-2.5 text-sm font-semibold flex items-center justify-center gap-2 ${
              activeTab === "whatsapp" ? btnPrimary.className : btnSecondary.className
            }`}
            style={activeTab === "whatsapp" ? btnPrimary.style : btnSecondary.style}
          >
            <ChatBubbleLeftRightIcon className="w-4 h-4" />
            <span>Send PDF on WhatsApp</span>
          </button>

          <button
            type="button"
            onClick={() => setActiveTab("email")}
            className={`px-4 py-2.5 text-sm font-semibold flex items-center justify-center gap-2 ${
              activeTab === "email" ? btnPrimary.className : btnSecondary.className
            }`}
            style={activeTab === "email" ? btnPrimary.style : btnSecondary.style}
          >
            <EnvelopeIcon className="w-4 h-4" />
            <span>Send PDF via Email</span>
          </button>
        </div>

        {/* Body Content */}
        <div className="p-6 max-h-[65vh] overflow-y-auto">
          {loading ? (
            <div className="py-12 flex flex-col items-center justify-center gap-3">
              <ArrowPathIcon className="w-7 h-7 animate-spin text-blue-500" />
              <p className={`text-sm ${isDark ? "text-slate-400" : "text-gray-500"}`}>
                Generating A4 PDF document ({meta?.filename || "PDF"})...
              </p>
            </div>
          ) : activeTab === "whatsapp" ? (
            <div className="space-y-4">
              <div>
                <div className="flex items-center justify-between mb-1.5">
                  <label className="text-xs font-semibold uppercase tracking-wider">
                    WhatsApp Phone Number
                  </label>
                  {normalizedPhone && (
                    <span className="text-xs font-mono text-emerald-600 dark:text-emerald-400">
                      +{normalizedPhone}
                    </span>
                  )}
                </div>
                <input
                  type="text"
                  value={phoneInput}
                  onChange={(e) => setPhoneInput(e.target.value)}
                  placeholder="e.g. 0300-1234567 or +923001234567"
                  className={inputClass}
                />
                <p className={`mt-1 text-xs ${isDark ? "text-slate-400" : "text-gray-500"}`}>
                  Pakistani mobile numbers (e.g. <code>0300...</code>) are automatically formatted with country code <code>92</code>.
                </p>
              </div>

              <div>
                <div className="flex items-center justify-between mb-1.5">
                  <label className="text-xs font-semibold uppercase tracking-wider">
                    PDF Caption / Message (No Link — Actual PDF File Used)
                  </label>
                  <button
                    type="button"
                    onClick={handleCopyWhatsAppMessage}
                    className="text-xs font-semibold text-blue-600 dark:text-blue-400 hover:underline flex items-center gap-1"
                  >
                    <ClipboardDocumentCheckIcon className="w-4 h-4" />
                    Copy Caption
                  </button>
                </div>
                <textarea
                  rows={7}
                  value={whatsappMessage}
                  onChange={(e) => setWhatsappMessage(e.target.value)}
                  className={`${inputClass} font-mono text-xs leading-relaxed`}
                />
                <p className={`mt-1 text-xs ${isDark ? "text-slate-400" : "text-gray-500"}`}>
                  📄 PDF File: <strong>{pdfFile?.name || meta?.filename || "Document.pdf"}</strong>{" "}
                  {meta?.whatsapp_api_configured
                    ? "(Will be sent directly as a WhatsApp PDF document via Cloud API)"
                    : "(Builds & saves the actual PDF file and opens the recipient's WhatsApp chat)"}
                </p>
              </div>

              <div className="flex flex-wrap items-center justify-end gap-3 pt-2">
                {canUseNativeFileShare && (
                  <button
                    type="button"
                    onClick={handleNativeOsFileShare}
                    className={`px-4 py-2.5 text-sm font-semibold flex items-center gap-2 ${btnSecondary.className}`}
                    style={btnSecondary.style}
                    title="Share the actual PDF file directly to WhatsApp Desktop/Mobile via OS Share Sheet"
                  >
                    <DocumentTextIcon className="w-4 h-4" />
                    <span>Share PDF File (OS Share)</span>
                  </button>
                )}

                <button
                  type="button"
                  disabled={sendingWhatsApp || buildingPdf}
                  onClick={handleSendWhatsAppPdf}
                  className={`px-4 py-2.5 text-sm font-semibold flex items-center gap-2 ${btnPrimary.className}`}
                  style={btnPrimary.style}
                >
                  {sendingWhatsApp ? (
                    <ArrowPathIcon className="w-4 h-4 animate-spin" />
                  ) : (
                    <ChatBubbleLeftRightIcon className="w-4 h-4" />
                  )}
                  <span>
                    {sendingWhatsApp
                      ? "Building & Sending PDF..."
                      : meta?.whatsapp_api_configured
                      ? "Send PDF Document on WhatsApp"
                      : "Build PDF & Open WhatsApp"}
                  </span>
                </button>
              </div>
            </div>
          ) : (
            <form onSubmit={handleSendEmail} className="space-y-4">
              <div>
                <label className="block text-xs font-semibold uppercase tracking-wider mb-1.5">
                  Recipient Email Address *
                </label>
                <input
                  type="email"
                  required
                  value={emailInput}
                  onChange={(e) => setEmailInput(e.target.value)}
                  placeholder="customer@example.com"
                  className={inputClass}
                />
                {meta?.party_id && (
                  <label className="mt-2 inline-flex items-center gap-2 text-xs cursor-pointer select-none">
                    <input
                      type="checkbox"
                      checked={saveEmailToProfile}
                      onChange={(e) => setSaveEmailToProfile(e.target.checked)}
                      className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                    />
                    <span className={isDark ? "text-slate-300" : "text-gray-600"}>
                      Save this email address to {meta.recipient_name}&apos;s profile
                    </span>
                  </label>
                )}
              </div>

              <div>
                <label className="block text-xs font-semibold uppercase tracking-wider mb-1.5">
                  Email Subject *
                </label>
                <input
                  type="text"
                  required
                  value={emailSubject}
                  onChange={(e) => setEmailSubject(e.target.value)}
                  className={inputClass}
                />
              </div>

              <div>
                <label className="block text-xs font-semibold uppercase tracking-wider mb-1.5">
                  Email Message Body *
                </label>
                <textarea
                  rows={6}
                  required
                  value={emailBody}
                  onChange={(e) => setEmailBody(e.target.value)}
                  className={inputClass}
                />
                <p className={`mt-1 text-xs ${isDark ? "text-slate-400" : "text-gray-500"}`}>
                  📎 Attached PDF File: <strong>{pdfFile?.name || meta?.filename || "Document.pdf"}</strong>
                </p>
              </div>

              <div className="flex items-center justify-end gap-3 pt-2">
                <button
                  type="submit"
                  disabled={sendingEmail}
                  className={`px-4 py-2.5 text-sm font-semibold flex items-center gap-2 ${btnPrimary.className}`}
                  style={btnPrimary.style}
                >
                  {sendingEmail ? (
                    <ArrowPathIcon className="w-4 h-4 animate-spin" />
                  ) : (
                    <PaperAirplaneIcon className="w-4 h-4" />
                  )}
                  <span>{sendingEmail ? "Sending PDF..." : "Send Email with PDF"}</span>
                </button>
              </div>
            </form>
          )}
        </div>

        {/* Modal Footer: Direct PDF Download & Preview */}
        <div
          className={`flex flex-wrap items-center justify-between gap-3 px-6 py-4 border-t ${
            isDark ? "border-slate-700 bg-slate-900/50" : "border-gray-200 bg-gray-50"
          }`}
        >
          <div className="flex items-center gap-2.5">
            <button
              type="button"
              disabled={!pdfBlobUrl || buildingPdf}
              onClick={() => window.open(pdfBlobUrl, "_blank", "noopener,noreferrer")}
              className={`px-4 py-2.5 text-sm font-semibold flex items-center gap-2 ${btnSecondary.className}`}
              style={btnSecondary.style}
            >
              <EyeIcon className="w-4 h-4" />
              <span>Open PDF in New Tab</span>
            </button>
            <button
              type="button"
              disabled={!pdfFile || buildingPdf}
              onClick={handleDownloadBuiltPdf}
              className={`px-4 py-2.5 text-sm font-semibold flex items-center gap-2 ${btnSecondary.className}`}
              style={btnSecondary.style}
            >
              <DocumentArrowDownIcon className="w-4 h-4" />
              <span>Download PDF File</span>
            </button>
          </div>

          <button
            type="button"
            onClick={onClose}
            className={`px-4 py-2.5 text-sm font-semibold ${btnSecondary.className}`}
            style={btnSecondary.style}
          >
            Close
          </button>
        </div>
      </div>
    </div>
  );
}
