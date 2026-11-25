import React, { useMemo, useState } from "react";
import {
  BookOpenIcon,
  MagnifyingGlassIcon,
  ArrowTopRightOnSquareIcon,
  LanguageIcon,
} from "@heroicons/react/24/outline";
import { useTheme } from "@/context/ThemeContext.jsx";

const RAW_DOCS = import.meta.glob("../../../docs/{en,ur,customer}/**/*.md", {
  query: "?raw",
  import: "default",
  eager: true,
});

const GITHUB_DOCS_URL =
  "https://github.com/shzdasim/Karobar-App/tree/perf/backend-db-optimization/docs";

function extractTitle(markdown, fallback) {
  const match = String(markdown || "").match(/^#\s+(.+)$/m);
  return match ? match[1].trim() : fallback;
}

function classifyDoc(relPath) {
  if (relPath.includes("/modules/")) return "modules";
  if (relPath.includes("/workflows/")) return "workflows";
  if (relPath.includes("/training/")) return "training";
  return "guides";
}

function renderInline(text) {
  const parts = String(text || "").split(/(`[^`]+`|\*\*[^*]+\*\*|\[[^\]]+\]\([^)]+\))/g);
  return parts.map((part, i) => {
    if (part.startsWith("`") && part.endsWith("`")) {
      return (
        <code
          key={i}
          className="px-1.5 py-0.5 rounded text-xs font-mono bg-slate-100 dark:bg-slate-800 text-indigo-700 dark:text-indigo-300"
        >
          {part.slice(1, -1)}
        </code>
      );
    }
    if (part.startsWith("**") && part.endsWith("**")) {
      return (
        <strong key={i} className="font-semibold">
          {part.slice(2, -2)}
        </strong>
      );
    }
    const linkMatch = part.match(/^\[([^\]]+)\]\(([^)]+)\)$/);
    if (linkMatch) {
      return (
        <span key={i} className="underline decoration-dotted text-blue-600 dark:text-blue-400">
          {linkMatch[1]}
        </span>
      );
    }
    return part;
  });
}

function MarkdownView({ content, isUrdu }) {
  const lines = String(content || "").split(/\r?\n/);
  const blocks = [];
  let i = 0;

  while (i < lines.length) {
    const line = lines[i];

    // Skip image-only markdown lines pointing to local relative Screenshots/
    if (/^!\[[^\]]*\]\([^)]+\)\s*$/.test(line.trim())) {
      i++;
      continue;
    }

    if (line.trim() === "" || line.trim() === "---") {
      i++;
      continue;
    }

    // Markdown table
    if (line.includes("|") && i + 1 < lines.length && /^\s*\|?[\s:-]+\|/.test(lines[i + 1])) {
      const parseRow = (r) =>
        r
          .trim()
          .replace(/^\|/, "")
          .replace(/\|$/, "")
          .split("|")
          .map((c) => c.trim());
      const headers = parseRow(line);
      i += 2;
      const rows = [];
      while (i < lines.length && lines[i].includes("|") && lines[i].trim() !== "") {
        rows.push(parseRow(lines[i]));
        i++;
      }
      blocks.push(
        <div key={`tbl-${i}`} className="overflow-x-auto my-3 rounded-lg border border-slate-200 dark:border-slate-700">
          <table className="w-full text-xs sm:text-sm border-collapse">
            <thead className="bg-slate-100 dark:bg-slate-800/80">
              <tr>
                {headers.map((h, idx) => (
                  <th key={idx} className="px-3 py-2 text-start font-semibold border-b border-slate-200 dark:border-slate-700">
                    {renderInline(h)}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-200 dark:divide-slate-700/70">
              {rows.map((r, rIdx) => (
                <tr key={rIdx} className="hover:bg-slate-50/70 dark:hover:bg-slate-800/40">
                  {r.map((cell, cIdx) => (
                    <td key={cIdx} className="px-3 py-2 align-top">
                      {renderInline(cell)}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      );
      continue;
    }

    // Headings
    const hMatch = line.match(/^(#{1,4})\s+(.+)$/);
    if (hMatch) {
      const level = hMatch[1].length;
      const cls =
        level === 1
          ? "text-xl sm:text-2xl font-bold mb-3 pb-2 border-b border-slate-200 dark:border-slate-700"
          : level === 2
          ? "text-lg font-bold mt-5 mb-2 text-blue-700 dark:text-blue-300"
          : "text-base font-semibold mt-4 mb-1.5";
      blocks.push(
        <div key={`h-${i}`} className={cls}>
          {renderInline(hMatch[2])}
        </div>
      );
      i++;
      continue;
    }

    // Blockquote
    if (line.startsWith(">")) {
      blocks.push(
        <blockquote
          key={`bq-${i}`}
          className="my-2 px-3 py-2 rounded-lg border-s-4 border-blue-500 bg-blue-50/60 dark:bg-blue-950/30 text-xs sm:text-sm"
        >
          {renderInline(line.replace(/^>\s*/, ""))}
        </blockquote>
      );
      i++;
      continue;
    }

    // Ordered or unordered list
    if (/^\s*([-*]|\d+\.)\s+/.test(line)) {
      const ordered = /^\s*\d+\./.test(line);
      const items = [];
      while (i < lines.length && /^\s*([-*]|\d+\.)\s+/.test(lines[i])) {
        items.push(lines[i].replace(/^\s*([-*]|\d+\.)\s+/, ""));
        i++;
      }
      const Tag = ordered ? "ol" : "ul";
      blocks.push(
        <Tag
          key={`list-${i}`}
          className={`my-2 space-y-1 ps-5 text-sm ${ordered ? "list-decimal" : "list-disc"}`}
        >
          {items.map((it, idx) => (
            <li key={idx}>{renderInline(it)}</li>
          ))}
        </Tag>
      );
      continue;
    }

    blocks.push(
      <p key={`p-${i}`} className="my-2 text-sm leading-relaxed">
        {renderInline(line)}
      </p>
    );
    i++;
  }

  return (
    <div dir={isUrdu ? "rtl" : "ltr"} className={isUrdu ? "font-sans leading-loose" : ""}>
      {blocks}
    </div>
  );
}

export default function UserManual() {
  const { isDark } = useTheme();
  const [lang, setLang] = useState("en");
  const [category, setCategory] = useState("all");
  const [search, setSearch] = useState("");

  const docs = useMemo(() => {
    const list = [];
    for (const [fullPath, content] of Object.entries(RAW_DOCS)) {
      const norm = fullPath.replace(/\\/g, "/");
      const marker = "/docs/";
      const idx = norm.indexOf(marker);
      const rel = idx >= 0 ? norm.slice(idx + marker.length) : norm;
      const docLang = rel.startsWith("ur/") || rel.startsWith("customer/ur/") ? "ur" : "en";
      const cat = classifyDoc(rel);
      const title = extractTitle(content, rel.split("/").pop());
      list.push({ id: rel, rel, lang: docLang, category: cat, title, content });
    }
    return list.sort((a, b) => a.title.localeCompare(b.title));
  }, []);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return docs.filter((d) => {
      if (d.lang !== lang) return false;
      if (category !== "all" && d.category !== category) return false;
      if (q && !d.title.toLowerCase().includes(q) && !d.content.toLowerCase().includes(q)) return false;
      return true;
    });
  }, [docs, lang, category, search]);

  const [selectedId, setSelectedId] = useState(null);
  const activeDoc = useMemo(() => {
    return filtered.find((d) => d.id === selectedId) || filtered[0] || null;
  }, [filtered, selectedId]);

  const isUrdu = lang === "ur";

  return (
    <div className="space-y-4">
      {/* Header */}
      <div className="g-card p-4 flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <div className="p-2.5 rounded-xl bg-blue-50 dark:bg-blue-950/50 text-blue-600 dark:text-blue-400">
            <BookOpenIcon className="w-6 h-6" />
          </div>
          <div>
            <h1 className="text-xl font-bold text-[var(--workspace-ink)]">
              {isUrdu ? "کاروبار ایپ — مکمل صارف مینوئل" : "Karobar-App — User Manual & Training"}
            </h1>
            <p className="text-xs text-[var(--workspace-muted)]">
              {isUrdu
                ? "فارمیسی مالکان، کیشیئرز اور اسٹاف کے لیے مکمل دو لسانی رہنمائی"
                : "Offline & GitHub-synced bilingual guide for pharmacy owners, cashiers, and staff"}
            </p>
          </div>
        </div>

        <div className="flex items-center gap-2 flex-wrap">
          <button
            type="button"
            onClick={() => {
              setLang((prev) => (prev === "en" ? "ur" : "en"));
              setSelectedId(null);
            }}
            className="theme-btn-secondary inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold cursor-pointer"
          >
            <LanguageIcon className="w-4 h-4" />
            <span>{isUrdu ? "Switch to English" : "اردو میں دیکھیں"}</span>
          </button>

          <a
            href={GITHUB_DOCS_URL}
            target="_blank"
            rel="noopener noreferrer"
            className="theme-btn-primary inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold"
          >
            <span>{isUrdu ? "GitHub پر کھولیں" : "View on GitHub"}</span>
            <ArrowTopRightOnSquareIcon className="w-3.5 h-3.5" />
          </a>
        </div>
      </div>

      {/* Main Split View */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-4">
        {/* Sidebar List */}
        <div className="lg:col-span-4 g-card p-3 space-y-3 h-fit lg:sticky lg:top-4">
          <div className="relative">
            <MagnifyingGlassIcon className="w-4 h-4 text-slate-400 absolute left-3 top-1/2 -translate-y-1/2" />
            <input
              type="text"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder={isUrdu ? "مینوئل میں تلاش کریں…" : "Search manual, guides, FAQ…"}
              className={`w-full pl-9 pr-3 py-2 text-xs rounded-lg border ${
                isDark
                  ? "bg-slate-900 border-slate-700 text-slate-100"
                  : "bg-white border-slate-200 text-slate-800"
              }`}
            />
          </div>

          <div className="flex flex-wrap gap-1">
            {[
              { id: "all", label: isUrdu ? "سب" : "All" },
              { id: "modules", label: isUrdu ? "ماڈیولز" : "Modules" },
              { id: "workflows", label: isUrdu ? "روزمرہ کام" : "How-To" },
              { id: "training", label: isUrdu ? "ٹریننگ" : "Training" },
              { id: "guides", label: isUrdu ? "سوالات / گائیڈز" : "FAQ & Guides" },
            ].map((tab) => (
              <button
                key={tab.id}
                type="button"
                onClick={() => setCategory(tab.id)}
                className={`px-2.5 py-1 rounded-md text-xs font-semibold cursor-pointer ${
                  category === tab.id
                    ? "theme-btn-primary"
                    : "text-[var(--workspace-muted)] hover:bg-slate-100 dark:hover:bg-slate-800"
                }`}
              >
                {tab.label}
              </button>
            ))}
          </div>

          <div className="max-h-[60vh] overflow-y-auto divide-y divide-[var(--workspace-border)]">
            {filtered.map((doc) => {
              const active = activeDoc?.id === doc.id;
              return (
                <button
                  key={doc.id}
                  type="button"
                  onClick={() => setSelectedId(doc.id)}
                  dir={isUrdu ? "rtl" : "ltr"}
                  className={`w-full text-start px-3 py-2 text-xs transition-colors cursor-pointer ${
                    active
                      ? "bg-blue-50 dark:bg-blue-950/50 text-blue-700 dark:text-blue-300 font-semibold"
                      : "text-[var(--workspace-ink)] hover:bg-slate-50 dark:hover:bg-slate-800/50"
                  }`}
                >
                  <div className="truncate">{doc.title}</div>
                  <div className="text-[10px] text-[var(--workspace-muted)] truncate">{doc.rel}</div>
                </button>
              );
            })}
            {filtered.length === 0 && (
              <p className="py-6 text-center text-xs text-[var(--workspace-muted)]">
                {isUrdu ? "کوئی دستاویز نہیں ملی۔" : "No matching guides found."}
              </p>
            )}
          </div>
        </div>

        {/* Document Reader */}
        <div className="lg:col-span-8 g-card p-5 sm:p-6 min-h-[60vh]">
          {activeDoc ? (
            <MarkdownView content={activeDoc.content} isUrdu={isUrdu} />
          ) : (
            <p className="text-sm text-[var(--workspace-muted)]">
              {isUrdu ? "پڑھنے کے لیے بائیں جانب سے کوئی گائیڈ منتخب کریں۔" : "Select a guide from the left to read."}
            </p>
          )}
        </div>
      </div>
    </div>
  );
}
