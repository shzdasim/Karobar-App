// resources/js/Formula/TradeScheme.js

/**
 * Evaluates trade scheme eligibility and calculates bonus for a line item.
 * Supports both Wholesale (pack + unit sync) and Retail (units) sale invoices.
 *
 * @param {Object} item Line item row
 * @param {Array} activeSchemes List of active schemes from /api/trade-schemes/active-lookup
 * @param {Object} [productObj] Additional product master data if available (brand_id, category_id, pack_size)
 * @param {Object} [options] Options: { sale_type: 'wholesale' | 'retail' }
 * @returns {Object} { matched: boolean, scheme_id: number|null, scheme_name: string|null, pack_bonus: number|string, unit_bonus: number|string, bonus: number|string }
 */
export function evaluateTradeScheme(item, activeSchemes = [], productObj = null, options = {}) {
  if (!item || !item.product_id || !Array.isArray(activeSchemes) || activeSchemes.length === 0) {
    return {
      matched: false,
      scheme_id: null,
      scheme_name: null,
      pack_bonus: "",
      unit_bonus: "",
      bonus: "",
    };
  }

  // If user has explicitly customized or waived the bonus for this line, do not overwrite
  if (item.is_custom_bonus) {
    return {
      matched: Boolean(item.applied_scheme_id),
      scheme_id: item.applied_scheme_id || null,
      scheme_name: item.applied_scheme_name || null,
      pack_bonus: item.pack_bonus ?? "",
      unit_bonus: item.unit_bonus ?? "",
      bonus: item.bonus ?? "",
      overridden: true,
    };
  }

  const productId = Number(item.product_id);
  const brandId = Number(item.brand_id || productObj?.brand_id || 0) || null;
  const categoryId = Number(item.category_id || productObj?.category_id || 0) || null;
  const packSize = Math.max(1, Number(item.pack_size || productObj?.pack_size || 1));

  const isRetail =
    options?.sale_type === "retail" ||
    item.sale_type === "retail" ||
    (item.pack_quantity === undefined && item.unit_quantity === undefined);

  const packQty = Math.max(0, Number(item.pack_quantity || 0));
  const unitQty = Math.max(0, Math.floor(Number(item.unit_quantity || 0)));
  const totalUnits = Math.max(0, Math.floor(Number(item.quantity || (packQty * packSize + unitQty))));

  if (totalUnits <= 0 && packQty <= 0) {
    return {
      matched: false,
      scheme_id: null,
      scheme_name: null,
      pack_bonus: "",
      unit_bonus: "",
      bonus: "",
    };
  }

  // Priority 1: Direct Product match
  let matchedScheme = activeSchemes.find(
    (s) => s.target_type === "product" && Number(s.target_id) === productId
  );

  // Priority 2: Brand match
  if (!matchedScheme && brandId) {
    matchedScheme = activeSchemes.find(
      (s) => s.target_type === "brand" && Number(s.target_id) === brandId
    );
  }

  // Priority 3: Category match
  if (!matchedScheme && categoryId) {
    matchedScheme = activeSchemes.find(
      (s) => s.target_type === "category" && Number(s.target_id) === categoryId
    );
  }

  if (!matchedScheme || !Array.isArray(matchedScheme.slabs) || matchedScheme.slabs.length === 0) {
    return {
      matched: false,
      scheme_id: null,
      scheme_name: null,
      pack_bonus: "",
      unit_bonus: "",
      bonus: "",
    };
  }

  const bonusType = matchedScheme.bonus_type || "pack"; // 'pack' or 'unit'
  const sortedSlabs = [...matchedScheme.slabs].sort(
    (a, b) => Number(a.min_qty) - Number(b.min_qty)
  );

  let packBonus = 0;
  let unitBonus = 0;
  let bonusUnits = 0;
  let matchedSlab = null;

  if (isRetail) {
    // -------------------------------------------------------------
    // RETAIL MODE: The cashier inputs totalUnits in a single column
    // -------------------------------------------------------------
    if (matchedScheme.scheme_type === "ratio") {
      const firstSlab = sortedSlabs[0];
      const minQ = Number(firstSlab?.min_qty || 0);
      const bQ = Number(firstSlab?.bonus_qty || 0);

      if (minQ > 0 && totalUnits >= minQ) {
        const multiplier = Math.floor(totalUnits / minQ);
        if (multiplier > 0) {
          bonusUnits = multiplier * bQ;
          matchedSlab = firstSlab;
        }
      }
    } else {
      // Tiered Slabs
      for (const slab of sortedSlabs) {
        const sMin = Number(slab.min_qty || 0);
        const sMax = slab.max_qty !== null && slab.max_qty !== undefined && slab.max_qty !== ""
          ? Number(slab.max_qty)
          : null;

        // If slab was configured in packs and customer bought at least sMin packs in units
        const qualifying = bonusType === "pack" && totalUnits >= sMin * packSize
          ? Math.floor(totalUnits / packSize)
          : totalUnits;

        if (qualifying >= sMin && (sMax === null || qualifying <= sMax)) {
          bonusUnits = bonusType === "pack" && totalUnits >= sMin * packSize
            ? Math.round(Number(slab.bonus_qty || 0) * packSize)
            : Math.round(Number(slab.bonus_qty || 0));
          matchedSlab = slab;
          break;
        }
      }
    }

    unitBonus = bonusUnits;
    packBonus = packSize > 0 ? Number((bonusUnits / packSize).toFixed(2)) : 0;
  } else {
    // -------------------------------------------------------------
    // WHOLESALE MODE: Dual columns (pack_quantity + unit_quantity)
    // -------------------------------------------------------------
    const qualifyingPacks = packQty + (packSize > 0 ? unitQty / packSize : 0);

    if (matchedScheme.scheme_type === "ratio") {
      const firstSlab = sortedSlabs[0];
      const minQ = Number(firstSlab?.min_qty || 0);
      const bQ = Number(firstSlab?.bonus_qty || 0);

      if (minQ > 0) {
        if (bonusType === "pack") {
          if (packQty >= minQ) {
            const multiplier = Math.floor(packQty / minQ);
            packBonus = multiplier * bQ;
            unitBonus = Math.round(packBonus * packSize);
            bonusUnits = unitBonus;
            matchedSlab = firstSlab;
          } else if (qualifyingPacks >= minQ) {
            // Combined packs + loose units reach min packs
            const multiplier = Math.floor(qualifyingPacks / minQ);
            packBonus = multiplier * bQ;
            unitBonus = Math.round(packBonus * packSize);
            bonusUnits = unitBonus;
            matchedSlab = firstSlab;
          } else if (unitQty >= minQ) {
            // Loose units alone qualify for the ratio
            const multiplier = Math.floor(unitQty / minQ);
            unitBonus = multiplier * bQ;
            packBonus = packSize > 0 ? Number((unitBonus / packSize).toFixed(2)) : 0;
            bonusUnits = unitBonus;
            matchedSlab = firstSlab;
          }
        } else {
          // Unit bonus type
          if (totalUnits >= minQ) {
            const multiplier = Math.floor(totalUnits / minQ);
            unitBonus = multiplier * bQ;
            packBonus = packSize > 0 ? Number((unitBonus / packSize).toFixed(2)) : 0;
            bonusUnits = unitBonus;
            matchedSlab = firstSlab;
          }
        }
      }
    } else {
      // Tiered Slabs
      const qualifyingQty = bonusType === "pack" ? qualifyingPacks : totalUnits;

      for (const slab of sortedSlabs) {
        const minQ = Number(slab.min_qty || 0);
        const maxQ = slab.max_qty !== null && slab.max_qty !== undefined && slab.max_qty !== ""
          ? Number(slab.max_qty)
          : null;

        if (qualifyingQty >= minQ && (maxQ === null || qualifyingQty <= maxQ)) {
          const awarded = Number(slab.bonus_qty || 0);
          matchedSlab = slab;
          if (bonusType === "pack") {
            packBonus = awarded;
            unitBonus = Math.round(awarded * packSize);
            bonusUnits = unitBonus;
          } else {
            unitBonus = Math.round(awarded);
            packBonus = packSize > 0 ? Number((awarded / packSize).toFixed(2)) : 0;
            bonusUnits = unitBonus;
          }
          break;
        }
      }
    }
  }

  if ((packBonus <= 0 && unitBonus <= 0 && bonusUnits <= 0) || !matchedSlab) {
    return {
      matched: true,
      scheme_id: matchedScheme.id,
      scheme_name: matchedScheme.name,
      pack_bonus: "",
      unit_bonus: "",
      bonus: "",
      pendingSlab: true, // Scheme exists on product, but threshold not reached yet
    };
  }

  return {
    matched: true,
    scheme_id: matchedScheme.id,
    scheme_name: matchedScheme.name,
    scheme_type: matchedScheme.scheme_type,
    bonus_type: bonusType,
    pack_bonus: packBonus > 0 ? String(packBonus) : "",
    unit_bonus: unitBonus > 0 ? String(unitBonus) : "",
    bonus: bonusUnits > 0 ? String(bonusUnits) : "",
    bonus_product_id: matchedSlab.bonus_product_id || productId,
    bonus_product: matchedSlab.bonus_product || matchedSlab.bonusProduct || null,
  };
}
