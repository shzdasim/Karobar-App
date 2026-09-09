// resources/js/components/settings/SaleSystemSetting.jsx
import { useTheme } from "@/context/ThemeContext";
import { useSaleSystem } from "@/context/SaleSystemContext.jsx";
import { GlassCard } from "@/components/glass.jsx";
import {
  ShoppingCartIcon,
  BanknotesIcon,
  ScaleIcon,
  CheckIcon,
  CheckBadgeIcon,
  DocumentCurrencyDollarIcon,
  ArrowTrendingUpIcon,
  InformationCircleIcon,
  SparklesIcon,
} from "@heroicons/react/24/solid";

// Feature sets shown for each sale system mode
const FEATURES = {
  retail: {
    icon: BanknotesIcon,
    accent: 'blue',
    title: 'Retail Only',
    tagline: 'Simple counter sales — one price per product',
    description:
      'Best for walk-in / counter businesses. One selling price per product and a clean, fast sale invoice workflow with no wholesale clutter.',
    bullets: [
      'Single selling price for every product',
      'Clean, fast sale invoice entry',
      'No wholesale price columns (pack / unit / margin %)',
      'No wholesale actions on the sale invoice list',
      'Hides wholesale-only pages and routes',
    ],
  },
  retail_wholesale: {
    icon: ScaleIcon,
    accent: 'purple',
    title: 'Retail + Wholesale',
    tagline: 'Full functionality — retail and bulk sales together',
    description:
      'Best for businesses that also sell in bulk. Adds wholesale pricing, wholesale invoices and full wholesale customer support while keeping all retail features.',
    bullets: [
      'Everything in Retail Only',
      'Wholesale price per product — pack, unit & wholesale margin %',
      'Wholesale / bulk sale invoices',
      'Wholesale columns in purchase invoice entry for accurate stock & margin',
      'Full wholesale customer support with ledgers',
    ],
  },
};

export default function SaleSystemSetting({
  form,
  handleSaleSystemChange,
  disableInputs,
  themeColors,
}) {
  const { isDark } = useTheme();
  const { saleSystem: activeSaleSystem, hasWholesale, loading: saleSystemLoading } = useSaleSystem();

  // Use passed themeColors if available, otherwise use defaults
  const colors = themeColors || {
    primary: '#3b82f6',
    primaryHover: '#2563eb',
    primaryLight: '#dbeafe',
    secondary: '#8b5cf6',
    secondaryHover: '#7c3aed',
    secondaryLight: '#ede9fe',
  };

  // Pending selection (from the settings form) falls back to the saved system
  const selected = form?.sale_system || activeSaleSystem || 'retail_wholesale';

  const activeLabel = hasWholesale ? 'Retail + Wholesale' : 'Retail Only';
  const activeNote = hasWholesale
    ? 'Wholesale is enabled — you can sell both retail and in bulk.'
    : 'Retail only — wholesale columns and bulk sale actions are hidden.';

  return (
    <div className="p-4 space-y-3">
      {/* ===== Header ===== */}
      <GlassCard>
        <div className="flex items-center gap-2 px-3 py-2 border-b border-gray-100 dark:border-slate-700">
          <div
            className="p-1.5 rounded-lg shadow-sm"
            style={{ background: `linear-gradient(to bottom right, ${colors.primary}, ${colors.primaryHover})` }}
          >
            <ShoppingCartIcon className="w-4 h-4 text-white" />
          </div>
          <div>
            <h2 className="text-sm font-semibold text-gray-900 dark:text-white">Sale System Configuration</h2>
            <p className="text-xs text-gray-500 dark:text-gray-400">
              Choose how your business handles sales — retail only, or retail + wholesale.
            </p>
          </div>
        </div>

        {/* Currently active mode (from live context) */}
        <div className="p-3 flex items-center gap-3 flex-wrap rounded-xl bg-gray-50/60 dark:bg-slate-800/40 ring-1 ring-gray-200/60 dark:ring-slate-600/60">
          <span className="text-xs font-medium text-gray-500 dark:text-gray-400">Currently active:</span>
          {saleSystemLoading ? (
            <span className="text-xs text-gray-500 dark:text-gray-400 animate-pulse">Loading…</span>
          ) : (
            <span
              className={`inline-flex items-center gap-1 px-2.5 py-1 text-xs font-semibold rounded-full ${
                hasWholesale
                  ? "bg-purple-50 text-purple-700 dark:bg-purple-900/30 dark:text-purple-300 ring-1 ring-purple-200 dark:ring-purple-700"
                  : "bg-blue-50 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300 ring-1 ring-blue-200 dark:ring-blue-700"
              }`}
            >
              <CheckBadgeIcon className="w-3.5 h-3.5" />
              {activeLabel}
            </span>
          )}
          <span className={`text-xs ${isDark ? "text-slate-400" : "text-gray-500"}`}>{activeNote}</span>
          {!saleSystemLoading && (
            <span className="ml-auto text-[11px] text-gray-400 dark:text-gray-500">
              Changes below apply instantly across the app once saved.
            </span>
          )}
        </div>
      </GlassCard>

      {/* ===== Mode Selection ===== */}
      <GlassCard>
        <div className="flex items-center gap-2 px-3 py-2 border-b border-gray-100 dark:border-slate-700">
          <SparklesIcon className="w-4 h-4 text-violet-500" />
          <h2 className="text-sm font-medium text-gray-900 dark:text-white">Choose a Sale System Mode</h2>
        </div>

        <div className="p-3 grid grid-cols-1 md:grid-cols-2 gap-3">
          {Object.entries(FEATURES).map(([key, feature]) => {
            const Icon = feature.icon;
            const isActive = selected === key;
            const accent = feature.accent; // 'blue' | 'purple'
            const activeBorder = accent === 'purple'
              ? 'border-purple-500 bg-purple-50 dark:bg-purple-900/20 ring-2 ring-purple-200 dark:ring-purple-800'
              : 'border-blue-500 bg-blue-50 dark:bg-blue-900/20 ring-2 ring-blue-200 dark:ring-blue-800';
            const textAccent = accent === 'purple'
              ? 'text-purple-700 dark:text-purple-400'
              : 'text-blue-700 dark:text-blue-400';

            return (
              <label
                key={key}
                className={`relative flex flex-col p-3 rounded-xl border-2 cursor-pointer transition-all duration-200 ${
                  isActive ? activeBorder : 'border-gray-200 dark:border-slate-600 hover:border-gray-300 dark:hover:border-slate-500'
                }`}
              >
                <input
                  type="radio"
                  name="sale_system"
                  value={key}
                  checked={isActive}
                  onChange={handleSaleSystemChange}
                  disabled={disableInputs}
                  className="sr-only"
                />

                {/* Title + radio indicator */}
                <div className="flex items-center gap-2 mb-1">
                  <div className={`w-4 h-4 rounded-full border-2 flex items-center justify-center ${
                    isActive
                      ? (accent === 'purple' ? 'border-purple-500 bg-purple-500' : 'border-blue-500 bg-blue-500')
                      : 'border-gray-300 dark:border-slate-500'
                  }`}>
                    {isActive && <div className="w-1.5 h-1.5 rounded-full bg-white" />}
                  </div>
                  <Icon className={`w-5 h-5 ${isActive ? textAccent : "text-gray-400"}`} />
                  <span className={`font-semibold text-sm ${isDark ? "text-slate-200" : "text-gray-800"}`}>
                    {feature.title}
                  </span>
                  {isActive && <CheckIcon className={`w-4 h-4 ml-auto ${textAccent}`} />}
                </div>

                <p className={`text-[11px] ${isDark ? "text-slate-400" : "text-gray-500"}`}>{feature.tagline}</p>
                <p className={`text-xs mt-1 leading-relaxed ${isDark ? "text-slate-300" : "text-gray-700"}`}>
                  {feature.description}
                </p>

                {/* Feature bullets */}
                <div className="mt-2 space-y-1">
                  {feature.bullets.map((bullet, i) => (
                    <div key={i} className="flex items-start gap-1.5">
                      <CheckIcon className={`w-3.5 h-3.5 shrink-0 ${isActive ? textAccent : "text-gray-400"}`} />
                      <span className={`text-[11px] leading-snug ${isDark ? "text-slate-400" : "text-gray-600"}`}>
                        {bullet}
                      </span>
                    </div>
                  ))}
                </div>
              </label>
            );
          })}
        </div>
      </GlassCard>

      {/* ===== What changes when you switch? ===== */}
      <GlassCard>
        <div className="flex items-center gap-2 px-3 py-2 border-b border-gray-100 dark:border-slate-700">
          <DocumentCurrencyDollarIcon className="w-4 h-4 text-emerald-500" />
          <h2 className="text-sm font-medium text-gray-900 dark:text-white">What changes when you switch</h2>
        </div>
        <div className="p-3 grid grid-cols-1 md:grid-cols-2 gap-3">
          <div className="rounded-lg bg-gray-50/60 dark:bg-slate-800/40 ring-1 ring-gray-200/60 dark:ring-slate-600/60 p-3">
            <div className="flex items-center gap-2 mb-2">
              <BanknotesIcon className="w-4 h-4 text-blue-500" />
              <span className={`text-xs font-semibold ${isDark ? "text-slate-300" : "text-gray-800"}`}>
                Retail Only mode shows you
              </span>
            </div>
            <ul className="space-y-1">
              {[
                'Single selling price per product',
                'Clean sale invoice — no wholesale unit / pack / margin columns',
                'Only retail actions on the sale invoice list',
                'Wholesale-only routes stay hidden / blocked',
              ].map((item, i) => (
                <li key={i} className="flex items-start gap-1.5">
                  <CheckIcon className="w-3.5 h-3.5 shrink-0 text-emerald-500" />
                  <span className={`text-[11px] leading-snug ${isDark ? "text-slate-400" : "text-gray-600"}`}>{item}</span>
                </li>
              ))}
            </ul>
          </div>
          <div className="rounded-lg bg-gray-50/60 dark:bg-slate-800/40 ring-1 ring-gray-200/60 dark:ring-slate-600/60 p-3">
            <div className="flex items-center gap-2 mb-2">
              <ArrowTrendingUpIcon className="w-4 h-4 text-purple-500" />
              <span className={`text-xs font-semibold ${isDark ? "text-slate-300" : "text-gray-800"}`}>
                Retail + Wholesale mode adds
              </span>
            </div>
            <ul className="space-y-1">
              {[
                'Wholesale price per product — pack, unit & margin %',
                'Wholesale columns inside purchase invoice entry',
                'Wholesale / bulk sale invoices and extra list actions',
                'Full wholesale customer workflow with ledgers',
              ].map((item, i) => (
                <li key={i} className="flex items-start gap-1.5">
                  <CheckIcon className="w-3.5 h-3.5 shrink-0 text-violet-500" />
                  <span className={`text-[11px] leading-snug ${isDark ? "text-slate-400" : "text-gray-600"}`}>{item}</span>
                </li>
              ))}
            </ul>
          </div>
        </div>
      </GlassCard>
{/* ===== Info note ===== */}
      <GlassCard>
        <div className="p-3 flex items-start gap-2 rounded-lg bg-blue-50/40 dark:bg-blue-900/15 ring-1 ring-blue-200/50 dark:ring-blue-800/30">
          <InformationCircleIcon className="w-5 h-5 shrink-0 text-blue-500" />
          <div>
            <p className={`text-xs font-medium ${isDark ? "text-slate-200" : "text-gray-800"}`}>How this works</p>
            <p className={`text-[11px] leading-relaxed ${isDark ? "text-slate-400" : "text-gray-600"}`}>
              Your selection is saved together with the rest of your settings. It takes effect immediately across the
              whole app — forms, invoice lists and pages adapt to the mode you choose. Use Retail + Wholesale if you
              sell in bulk; switch to Retail Only for a simpler, counter-only workflow.
            </p>
          </div>
        </div>
      </GlassCard>
    </div>
  );
}