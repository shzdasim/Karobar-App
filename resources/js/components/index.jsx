// Reusable components index
import React, { Suspense, lazy } from "react";

function withLazyModal(loader) {
  const LazyComp = lazy(loader);
  return function LazyModalWrapper(props) {
    const isOpen = props.isOpen ?? props.open ?? props.show ?? true;
    if (!isOpen) return null;
    return (
      <Suspense fallback={null}>
        <LazyComp {...props} />
      </Suspense>
    );
  };
}

export const DeleteConfirmationModal = withLazyModal(() => import("./DeleteConfirmationModal.jsx"));
export const BulkDeleteModal = withLazyModal(() => import("./BulkDeleteModal.jsx"));
export const BulkDeleteModalWithIds = withLazyModal(() =>
  import("./BulkDeleteModal.jsx").then((m) => ({ default: m.BulkDeleteModalWithIds || m.default }))
);
export const BulkEditModal = withLazyModal(() => import("./BulkEditModal.jsx"));
export const ProductImportModal = withLazyModal(() => import("./ProductImportModal.jsx"));
export const ProductFormModal = withLazyModal(() => import("./ProductFormModal.jsx"));
export const CustomerImportModal = withLazyModal(() => import("./CustomerImportModal.jsx"));
export const BrandImportModal = withLazyModal(() => import("./BrandImportModal.jsx"));
export const CategoryImportModel = withLazyModal(() => import("./CategoryImportModel.jsx"));
export const SupplierImportModal = withLazyModal(() => import("./SupplierImportModal.jsx"));
export const ProductSearchModal = withLazyModal(() => import("./ProductSearchModal.jsx"));
export const PageSearchModal = withLazyModal(() => import("./PageSearchModal.jsx"));

export { default as TextSearch } from "./TextSearch.jsx";
export { default as ProductSearch } from "./ProductSearch.jsx";
export { default as AccountIcon } from "./AccountIcon.jsx";
export { default as ProductSearchInput } from "./ProductSearchInput.jsx";
export { default as SupplierSearchInput } from "./SupplierSearchInput.jsx";
export { default as BatchSearchInput } from "./BatchSearchInput.jsx";
export { default as Sidebar } from "./Sidebar.jsx";
export { default as TopNavigation } from "./TopNavigation.jsx";
export { default as ThemeToggle } from "./ThemeToggle.jsx";
export { default as ProtectedRoute } from "./ProtectedRoute.jsx";
export { default as AuthGuard } from "./AuthGuard.jsx";
export { GlassCard, GlassSectionHeader, GlassToolbar, GlassInput, GlassBtn } from "./Glass.jsx";
