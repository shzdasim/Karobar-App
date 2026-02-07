import { MagnifyingGlassIcon } from "@heroicons/react/24/solid";
import { GlassInput } from "./Glass.jsx";

/**
 * Reusable text search input component
 * 
 * @param {Object} props
 * @param {string} props.value - Current search value
 * @param {Function} props.onChange - Function to call when value changes
 * @param {string} props.placeholder - Placeholder text
 * @param {React.ReactNode} props.icon - Optional icon to show on the left
 * @param {string} props.className - Additional CSS classes
 */
export default function TextSearch({
  value,
  onChange,
  placeholder = "Search…",
  icon,
  className = "",
}) {
  return (
    <div className={`relative ${className}`}>
      {icon ? (
        <span className="absolute left-3 top-1/2 -translate-y-1/2">{icon}</span>
      ) : (
        <MagnifyingGlassIcon className="w-5 h-5 absolute left-3 top-1/2 -translate-y-1/2 text-gray-400" />
      )}
      <GlassInput
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        className="pl-10 w-full"
      />
    </div>
  );
}

