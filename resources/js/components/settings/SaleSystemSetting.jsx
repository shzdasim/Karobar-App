// resources/js/components/settings/SaleSystemSetting.jsx
import { useSaleSystem } from "@/context/SaleSystemContext.jsx";
import {
  ShoppingCartIcon,
  BanknotesIcon,
  ScaleIcon,
  CheckIcon,
  CheckBadgeIcon,
  ArrowTrendingUpIcon,
  InformationCircleIcon,
  BeakerIcon,
  BuildingStorefrontIcon,
} from "@heroicons/react/24/solid";

// Feature sets shown for each sale system mode
const FEATURES = {
  retail: {
    icon: BanknotesIcon,
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

// Shop type choices shown for each kind of store
const SHOP_TYPES = {
  pharmacy: {
    icon: BeakerIcon,
    title: 'Pharmacy',
    tagline: 'Medicine, controlled items & prescription sales',
    description:
      'Best for medical / pharmacy stores. Unlocks pharmacy-specific fields like product formulation, the narcotic / controlled-product flag, and Doctor & Patient name fields on sale invoices.',
    bullets: [
      'Formulation field on product entry',
      'Narcotic / controlled-product flag',
      'Doctor & Patient name fields on sale invoices',
      'Doctor / Patient shown in search & reports',
    ],
  },
  general_store: {
    icon: BuildingStorefrontIcon,
    title: 'General Store',
    tagline: 'Everyday retail goods — no pharmacy-specific fields',
    description:
      'Best for general / grocery / non-pharmacy stores. Hides pharmacy-only fields so product entry and sale invoices stay clean and focused on standard pricing and stock.',
    bullets: [
      'No formulation field on product entry',
      'No narcotic / controlled-product flag',
      'No Doctor / Patient fields on sale invoices',
      'Clean, generic sale invoice workflow',
    ],
  },
};

function ChoiceGroup({ name, legend, options, selectedKey, onSelect, disableInputs }) {
  return (
    <div className="settings-field">
      <span className="settings-label">{legend}</span>
      <div className="settings-options settings-options-2">
        {Object.entries(options).map(([key, option]) => {
          const Icon = option.icon;
          const isActive = selectedKey === key;
          return (
            <label key={key} className={`settings-option ${isActive ? "is-active" : ""}`}>
              <input
                type="radio"
                name={name}
                value={key}
                checked={isActive}
                onChange={onSelect}
                disabled={disableInputs}
                className="sr-only"
              />

              <span className="settings-option-head">
                <Icon aria-hidden="true" />
                <span className="settings-option-title">{option.title}</span>
                {isActive && (
                  <CheckIcon
                    aria-hidden="true"
                    style={{ color: "var(--workspace-accent)", marginLeft: "auto" }}
                  />
                )}
              </span>

              <span className="settings-option-tagline">{option.tagline}</span>
              <span className="settings-option-note">{option.description}</span>

              <ul className="settings-bullets">
                {option.bullets.map((bullet, i) => (
                  <li key={i}>
                    <CheckIcon aria-hidden="true" />
                    <span>{bullet}</span>
                  </li>
                ))}
              </ul>
            </label>
          );
        })}
      </div>
    </div>
  );
}

export default function SaleSystemSetting({
  form,
  handleSaleSystemChange,
  handleShopTypeChange,
  disableInputs,
}) {
  const {
    saleSystem: activeSaleSystem,
    hasWholesale,
    shopType: activeShopType,
    isPharmacy,
    loading: saleSystemLoading,
  } = useSaleSystem();

  // Pending selection (from the settings form) falls back to the saved system
  const selected = form?.sale_system || activeSaleSystem || 'retail_wholesale';

  // Pending shop type falls back to the saved shop type
  const selectedShopType = form?.shop_type || activeShopType || 'pharmacy';

  const activeLabel = hasWholesale ? 'Retail + Wholesale' : 'Retail Only';
  const activeShopTypeLabel = isPharmacy ? 'Pharmacy' : 'General Store';
  const activeNote = hasWholesale
    ? 'Wholesale is enabled — you can sell both retail and in bulk.'
    : 'Retail only — wholesale columns and bulk sale actions are hidden.';

  return (
    <>
      {/* ===== Header + currently active ===== */}
      <div className="settings-block">
        <div className="settings-block-heading">
          <div className="flex items-center gap-3 min-w-0">
            <div className="settings-block-icon">
              <ShoppingCartIcon />
            </div>
            <div className="min-w-0">
              <h2 className="settings-block-title">Sale System Configuration</h2>
              <p className="settings-block-note">
                Choose your sale mode (retail / wholesale) and shop type (pharmacy / general store).
              </p>
            </div>
          </div>
        </div>

        <div className="settings-body">
          <div className="settings-field">
            <span className="settings-label">Currently active</span>
            <div className="flex flex-wrap items-center gap-3">
              {saleSystemLoading ? (
                <span className="settings-hint">Loading…</span>
              ) : (
                <>
                  <span
                    className="settings-chip"
                    style={{ "--settings-chip": "var(--color-primary)" }}
                  >
                    <CheckBadgeIcon aria-hidden="true" />
                    {activeLabel}
                  </span>
                  <span
                    className="settings-chip"
                    style={{ "--settings-chip": "var(--color-secondary)" }}
                  >
                    <CheckBadgeIcon aria-hidden="true" />
                    {activeShopTypeLabel}
                  </span>
                  <span className="settings-hint">{activeNote}</span>
                </>
              )}
            </div>
          </div>
        </div>
      </div>

      {/* ===== Mode selection ===== */}
      <div className="settings-block">
        <div className="settings-block-heading">
          <div className="flex items-center gap-3 min-w-0">
            <div className="settings-block-icon">
              <ScaleIcon />
            </div>
            <div className="min-w-0">
              <h2 className="settings-block-title">Sale System Mode</h2>
              <p className="settings-block-note">
                Changes below apply instantly across the app once saved.
              </p>
            </div>
          </div>
        </div>

        <div className="settings-body">
          <ChoiceGroup
            name="sale_system"
            legend="Choose a sale system mode"
            options={FEATURES}
            selectedKey={selected}
            onSelect={handleSaleSystemChange}
            disableInputs={disableInputs}
          />
        </div>
      </div>

      {/* ===== Shop type selection ===== */}
      <div className="settings-block">
        <div className="settings-block-heading">
          <div className="flex items-center gap-3 min-w-0">
            <div className="settings-block-icon">
              <BeakerIcon />
            </div>
            <div className="min-w-0">
              <h2 className="settings-block-title">Shop Type</h2>
              <p className="settings-block-note">
                Pharmacy fields stay available only for medical stores.
              </p>
            </div>
          </div>
        </div>

        <div className="settings-body">
          <ChoiceGroup
            name="shop_type"
            legend="Choose a shop type"
            options={SHOP_TYPES}
            selectedKey={selectedShopType}
            onSelect={handleShopTypeChange}
            disableInputs={disableInputs}
          />
        </div>
      </div>

      {/* ===== What changes when you switch ===== */}
      <div className="settings-block">
        <div className="settings-block-heading">
          <div className="flex items-center gap-3 min-w-0">
            <div className="settings-block-icon">
              <ArrowTrendingUpIcon />
            </div>
            <div className="min-w-0">
              <h2 className="settings-block-title">What changes when you switch</h2>
            </div>
          </div>
        </div>

        <div className="settings-body">
          <div className="settings-tiles">
            <div className="settings-tile">
              <div className="settings-tile-title">
                <BanknotesIcon aria-hidden="true" />
                Retail Only mode shows you
              </div>
              <ul className="settings-bullets">
                {[
                  'Single selling price per product',
                  'Clean sale invoice — no wholesale unit / pack / margin columns',
                  'Only retail actions on the sale invoice list',
                  'Wholesale-only routes stay hidden / blocked',
                ].map((item, i) => (
                  <li key={i}>
                    <CheckIcon aria-hidden="true" />
                    <span>{item}</span>
                  </li>
                ))}
              </ul>
            </div>

            <div className="settings-tile">
              <div className="settings-tile-title">
                <ScaleIcon aria-hidden="true" />
                Retail + Wholesale mode adds
              </div>
              <ul className="settings-bullets">
                {[
                  'Wholesale price per product — pack, unit & margin %',
                  'Wholesale columns inside purchase invoice entry',
                  'Wholesale / bulk sale invoices and extra list actions',
                  'Full wholesale customer workflow with ledgers',
                ].map((item, i) => (
                  <li key={i}>
                    <CheckIcon aria-hidden="true" />
                    <span>{item}</span>
                  </li>
                ))}
              </ul>
            </div>
          </div>

          <div className="settings-note">
            <InformationCircleIcon aria-hidden="true" />
            <div>
              <span className="settings-note-title">How this works</span>
              Your selection is saved together with the rest of your settings. It takes effect immediately across
              the whole app — forms, invoice lists and pages adapt to the mode you choose. Use Retail + Wholesale
              if you sell in bulk; switch to Retail Only for a simpler, counter-only workflow.
            </div>
          </div>
        </div>
      </div>
    </>
  );
}
