import React from "react";
import {
  BanknotesIcon,
  BuildingLibraryIcon,
  CreditCardIcon,
  DevicePhoneMobileIcon,
  WalletIcon,
} from "@heroicons/react/24/solid";

// SVG Brand Logos & Emblems for Pakistani Banks and Digital Wallets
export function AccountIcon({ icon, code, type, className = "w-6 h-6", size = 24 }) {
  const normalized = (icon || code || type || "").toLowerCase();

  // 1. Meezan Bank
  if (normalized.includes("meezan")) {
    return (
      <svg className={className} viewBox="0 0 32 32" fill="none" xmlns="http://www.w3.org/2000/svg">
        <rect width="32" height="32" rx="8" fill="#005B41" />
        <path d="M16 6L23 11V21L16 26L9 21V11L16 6Z" fill="#FBBF24" opacity="0.25" />
        <path d="M16 8.5L21.5 12.5V19.5L16 23.5L10.5 19.5V12.5L16 8.5Z" stroke="#FBBF24" strokeWidth="1.8" />
        <path d="M16 12V20M12.5 14L16 12L19.5 14" stroke="#FFFFFF" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" />
      </svg>
    );
  }

  // 2. HBL (Habib Bank Limited)
  if (normalized.includes("hbl") || normalized.includes("habib bank")) {
    return (
      <svg className={className} viewBox="0 0 32 32" fill="none" xmlns="http://www.w3.org/2000/svg">
        <rect width="32" height="32" rx="8" fill="#00835F" />
        <path d="M16 6L24.5 16L16 26L7.5 16L16 6Z" fill="#006346" />
        <path d="M16 8.5L22.5 16L16 23.5L9.5 16L16 8.5Z" stroke="#FFFFFF" strokeWidth="1.6" />
        <text x="16" y="19" textAnchor="middle" fill="#FFFFFF" fontSize="9" fontWeight="900" fontFamily="sans-serif">HBL</text>
      </svg>
    );
  }

  // 3. UBL (United Bank Limited)
  if (normalized.includes("ubl") || normalized.includes("united bank")) {
    return (
      <svg className={className} viewBox="0 0 32 32" fill="none" xmlns="http://www.w3.org/2000/svg">
        <rect width="32" height="32" rx="8" fill="#00539F" />
        <circle cx="16" cy="16" r="10" stroke="#00A3E0" strokeWidth="2" strokeDasharray="3 2" />
        <text x="16" y="19.5" textAnchor="middle" fill="#FFFFFF" fontSize="9.5" fontWeight="900" fontFamily="sans-serif">UBL</text>
      </svg>
    );
  }

  // 4. MCB Bank
  if (normalized.includes("mcb")) {
    return (
      <svg className={className} viewBox="0 0 32 32" fill="none" xmlns="http://www.w3.org/2000/svg">
        <rect width="32" height="32" rx="8" fill="#EB6909" />
        <circle cx="16" cy="16" r="11" fill="#FFFFFF" />
        <path d="M11 12H13.5L16 16.5L18.5 12H21V20H19V15L16.8 19H15.2L13 15V20H11V12Z" fill="#00539F" />
      </svg>
    );
  }

  // 5. Bank Alfalah
  if (normalized.includes("alfalah") || normalized.includes("alfa")) {
    return (
      <svg className={className} viewBox="0 0 32 32" fill="none" xmlns="http://www.w3.org/2000/svg">
        <rect width="32" height="32" rx="8" fill="#DC2626" />
        <path d="M16 7L24 23H19.5L16 15.5L12.5 23H8L16 7Z" fill="#FFFFFF" />
        <circle cx="16" cy="19.5" r="2" fill="#FBBF24" />
      </svg>
    );
  }

  // 6. Allied Bank (ABL)
  if (normalized.includes("abl") || normalized.includes("allied")) {
    return (
      <svg className={className} viewBox="0 0 32 32" fill="none" xmlns="http://www.w3.org/2000/svg">
        <rect width="32" height="32" rx="8" fill="#004B87" />
        <path d="M8 22L16 9L24 22H19.5L16 16L12.5 22H8Z" fill="#EA580C" />
        <path d="M12.5 22L16 16L19.5 22H12.5Z" fill="#FFFFFF" />
      </svg>
    );
  }

  // 7. Faysal Bank
  if (normalized.includes("faysal")) {
    return (
      <svg className={className} viewBox="0 0 32 32" fill="none" xmlns="http://www.w3.org/2000/svg">
        <rect width="32" height="32" rx="8" fill="#1A365D" />
        <circle cx="16" cy="16" r="10" stroke="#D97706" strokeWidth="2" />
        <path d="M12 12H20V15H15V17H19V19.5H15V22H12V12Z" fill="#FFFFFF" />
      </svg>
    );
  }

  // 8. Bank of Punjab (BOP)
  if (normalized.includes("bop") || normalized.includes("punjab")) {
    return (
      <svg className={className} viewBox="0 0 32 32" fill="none" xmlns="http://www.w3.org/2000/svg">
        <rect width="32" height="32" rx="8" fill="#B91C1C" />
        <rect x="7" y="7" width="18" height="18" rx="4" fill="#FFFFFF" />
        <text x="16" y="19" textAnchor="middle" fill="#B91C1C" fontSize="9" fontWeight="900" fontFamily="sans-serif">BOP</text>
      </svg>
    );
  }

  // 9. Standard Chartered (SCB)
  if (normalized.includes("scb") || normalized.includes("standard chartered")) {
    return (
      <svg className={className} viewBox="0 0 32 32" fill="none" xmlns="http://www.w3.org/2000/svg">
        <rect width="32" height="32" rx="8" fill="#0284C7" />
        <path d="M10 18C10 13.5 13.5 10 18 10C21 10 23.5 11.5 24.5 14L21.5 15.5C20.8 14 19.5 13 18 13C15.2 13 13 15.2 13 18C13 20.8 15.2 23 18 23C19.5 23 20.8 22 21.5 20.5L24.5 22C23.5 24.5 21 26 18 26C13.5 26 10 22.5 10 18Z" fill="#10B981" />
        <circle cx="19" cy="14" r="2.5" fill="#FFFFFF" />
      </svg>
    );
  }

  // 10. National Bank of Pakistan (NBP)
  if (normalized.includes("nbp") || normalized.includes("national bank")) {
    return (
      <svg className={className} viewBox="0 0 32 32" fill="none" xmlns="http://www.w3.org/2000/svg">
        <rect width="32" height="32" rx="8" fill="#047857" />
        <circle cx="16" cy="16" r="10" fill="#065F46" stroke="#FBBF24" strokeWidth="1.5" />
        <text x="16" y="19" textAnchor="middle" fill="#FFFFFF" fontSize="8.5" fontWeight="900" fontFamily="sans-serif">NBP</text>
      </svg>
    );
  }

  // 11. JazzCash
  if (normalized.includes("jazzcash") || normalized.includes("jazz")) {
    return (
      <svg className={className} viewBox="0 0 32 32" fill="none" xmlns="http://www.w3.org/2000/svg">
        <rect width="32" height="32" rx="8" fill="#DC2626" />
        <circle cx="16" cy="16" r="11" fill="#18181B" />
        <circle cx="16" cy="16" r="8" fill="#EF4444" />
        <path d="M14 11V18C14 19.5 15 20.5 16.5 20.5C18 20.5 19 19.5 19 18V11H17V18C17 18.5 16.8 18.8 16.5 18.8C16.2 18.8 16 18.5 16 18V11H14Z" fill="#FBBF24" />
      </svg>
    );
  }

  // 12. Easypaisa
  if (normalized.includes("easypaisa") || normalized.includes("easy paisa")) {
    return (
      <svg className={className} viewBox="0 0 32 32" fill="none" xmlns="http://www.w3.org/2000/svg">
        <rect width="32" height="32" rx="8" fill="#16A34A" />
        <circle cx="16" cy="16" r="10" fill="#FFFFFF" />
        <path d="M12 16C12 13.8 13.8 12 16 12C18.2 12 20 13.8 20 16C20 18.2 18.2 20 16 20C13.8 20 12 18.2 12 16Z" fill="#16A34A" />
        <circle cx="16" cy="16" r="2.2" fill="#FFFFFF" />
      </svg>
    );
  }

  // 13. SadaPay
  if (normalized.includes("sadapay") || normalized.includes("sada")) {
    return (
      <svg className={className} viewBox="0 0 32 32" fill="none" xmlns="http://www.w3.org/2000/svg">
        <rect width="32" height="32" rx="8" fill="#0D9488" />
        <rect x="7" y="7" width="18" height="18" rx="5" fill="#14B8A6" />
        <path d="M12 19C12 15 15 13 18 13C19.5 13 20.5 13.5 21 14.5L19 16C18.5 15.5 18 15 17.5 15C15.5 15 14.5 16.5 14.5 18.5C14.5 20.5 15.5 22 17.5 22C18 22 18.5 21.5 19 21L21 22.5C20.5 23.5 19.5 24 18 24C15 24 12 22 12 19Z" fill="#FFFFFF" />
      </svg>
    );
  }

  // 14. NayaPay
  if (normalized.includes("nayapay") || normalized.includes("naya")) {
    return (
      <svg className={className} viewBox="0 0 32 32" fill="none" xmlns="http://www.w3.org/2000/svg">
        <rect width="32" height="32" rx="8" fill="#EA580C" />
        <circle cx="16" cy="16" r="9" fill="#0284C7" />
        <path d="M12 21V11L20 21V11" stroke="#FFFFFF" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" />
      </svg>
    );
  }

  // 15. UPaisa
  if (normalized.includes("upaisa")) {
    return (
      <svg className={className} viewBox="0 0 32 32" fill="none" xmlns="http://www.w3.org/2000/svg">
        <rect width="32" height="32" rx="8" fill="#F97316" />
        <circle cx="16" cy="16" r="10" fill="#FFFFFF" />
        <text x="16" y="20" textAnchor="middle" fill="#F97316" fontSize="12" fontWeight="900" fontFamily="sans-serif">U</text>
      </svg>
    );
  }

  // 16. Zindigi
  if (normalized.includes("zindigi")) {
    return (
      <svg className={className} viewBox="0 0 32 32" fill="none" xmlns="http://www.w3.org/2000/svg">
        <rect width="32" height="32" rx="8" fill="#7C3AED" />
        <path d="M11 12H21L13 20H21" stroke="#F43F5E" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" />
      </svg>
    );
  }

  // 17. Raast P2M
  if (normalized.includes("raast")) {
    return (
      <svg className={className} viewBox="0 0 32 32" fill="none" xmlns="http://www.w3.org/2000/svg">
        <rect width="32" height="32" rx="8" fill="#047857" />
        <path d="M10 16L16 10L22 16L18 16L18 22L14 22L14 16L10 16Z" fill="#FBBF24" />
      </svg>
    );
  }

  // 18. Cash Drawer (Physical till / cash counter)
  if (normalized.includes("drawer") || normalized.includes("cash")) {
    return (
      <div className={`flex items-center justify-center rounded-lg bg-emerald-500 text-white shadow-xs ${className}`}>
        <BanknotesIcon className="w-4/5 h-4/5" />
      </div>
    );
  }

  // 19. Card / POS Terminal
  if (normalized.includes("card") || normalized.includes("pos")) {
    return (
      <div className={`flex items-center justify-center rounded-lg bg-purple-600 text-white shadow-xs ${className}`}>
        <CreditCardIcon className="w-4/5 h-4/5" />
      </div>
    );
  }

  // 20. Generic Bank
  if (type === "bank") {
    return (
      <div className={`flex items-center justify-center rounded-lg bg-blue-600 text-white shadow-xs ${className}`}>
        <BuildingLibraryIcon className="w-4/5 h-4/5" />
      </div>
    );
  }

  // 21. Generic Mobile Wallet
  if (type === "mobile_wallet") {
    return (
      <div className={`flex items-center justify-center rounded-lg bg-amber-500 text-white shadow-xs ${className}`}>
        <DevicePhoneMobileIcon className="w-4/5 h-4/5" />
      </div>
    );
  }

  // Default fallback
  return (
    <div className={`flex items-center justify-center rounded-lg bg-slate-600 text-white shadow-xs ${className}`}>
      <BuildingLibraryIcon className="w-4/5 h-4/5" />
    </div>
  );
}

export default AccountIcon;

