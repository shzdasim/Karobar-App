import React, { Suspense, lazy } from "react";
import { Routes, Route } from "react-router-dom";

import DashboardLayout from "../layouts/DashboardLayout.jsx";
import ProtectedRoute from "../components/ProtectedRoute.jsx";
import AuthGuard from "../components/AuthGuard.jsx";
import SaleSystemGuard from "../components/SaleSystemGuard.jsx";

const Login = lazy(() => import("../pages/Login.jsx"));
const ForgotPassword = lazy(() => import("../pages/ForgotPassword.jsx"));
const ResetPassword = lazy(() => import("../pages/ResetPassword.jsx"));
const Profile = lazy(() => import("../pages/Profile.jsx"));
const Suppliers = lazy(() => import("../pages/Suppliers.jsx"));
const Customers = lazy(() => import("../pages/Customers.jsx"));
const Categories = lazy(() => import("../pages/Categories.jsx"));
const Brands = lazy(() => import("../pages/Brands.jsx"));
const ProductsIndex = lazy(() => import("../pages/Products/index.jsx"));
const CreateProduct = lazy(() => import("../pages/Products/Create.jsx"));
const EditProduct = lazy(() => import("../pages/Products/Edit.jsx"));
const PurchaseInvoicesIndex = lazy(() => import("../pages/PurchaseInvoice/index.jsx"));
const CreatePurchaseInvoice = lazy(() => import("../pages/PurchaseInvoice/Create.jsx"));
const EditPurchaseInvoice = lazy(() => import("../pages/PurchaseInvoice/Edit.jsx"));
const ShowPurchaseInvoice = lazy(() => import("../pages/PurchaseInvoice/Show.jsx"));
const PurchaseReturnsIndex = lazy(() => import("../pages/PurchaseReturn/index.jsx"));
const CreatePurchaseReturn = lazy(() => import("../pages/PurchaseReturn/Create.jsx"));
const EditPurchaseReturn = lazy(() => import("../pages/PurchaseReturn/Edit.jsx"));
const IndexSaleInvoice = lazy(() => import("../pages/SaleInvoice/Index.jsx"));
const CreateSaleInvoiceRetail = lazy(() => import("../pages/SaleInvoice/CreateRetail.jsx"));
const CreateSaleInvoiceWholesale = lazy(() => import("../pages/SaleInvoice/CreateWholesale.jsx"));
const BarcodePos = lazy(() => import("../pages/SaleInvoice/BarcodePos.jsx"));
const EditSaleInvoice = lazy(() => import("../pages/SaleInvoice/Edit.jsx"));
const ShowSaleInvoice = lazy(() => import("../pages/SaleInvoice/Show.jsx"));
const IndexSaleReturn = lazy(() => import("../pages/SaleReturn/Index.jsx"));
const CreateSaleReturn = lazy(() => import("../pages/SaleReturn/Create.jsx"));
const EditSaleReturn = lazy(() => import("../pages/SaleReturn/Edit.jsx"));
const PurchaseOrders = lazy(() => import("../pages/PurchaseOrders.jsx"));
const PurchaseOrder = lazy(() => import("../pages/PurchaseOrder.jsx"));
const UserDemandsIndex = lazy(() => import("../pages/UserDemand/index.jsx"));
const CreateUserDemand = lazy(() => import("../pages/UserDemand/Create.jsx"));
const Dashboard = lazy(() => import("../pages/Dashboard.jsx"));
const CostOfSaleReport = lazy(() => import("../pages/Reports/CostOfSaleReport.jsx"));
const CostOfSaleDetailReport = lazy(() => import("../pages/Reports/CostOfSaleDetailReport.jsx"));
const PurchaseDetailReport = lazy(() => import("../pages/Reports/PurchaseDetailReport.jsx"));
const SaleDetailReport = lazy(() => import("../pages/Reports/SaleDetailReport.jsx"));
const CurrentStockReport = lazy(() => import("../pages/Reports/CurrentStockReport.jsx"));
const StockAdjustmentReport = lazy(() => import("../pages/Reports/StockAdjustmentReport.jsx"));
const ProductComprehensiveReport = lazy(() => import("../pages/Reports/ProductComprehensiveReport.jsx"));
const NearExpiryProductReport = lazy(() => import("../pages/Reports/NearExpiryProductReport.jsx"));
const BatchExpiryTracking = lazy(() => import("../pages/BatchExpiry/BatchExpiryTracking.jsx"));
const NetProfitReport = lazy(() => import("../pages/Reports/NetProfitReport.jsx"));
const ExpensesIndex = lazy(() => import("../pages/Expenses/index.jsx"));
const MissingReports = lazy(() => import("../pages/Reports/MissingReports.jsx"));
const Setting = lazy(() => import("../pages/Setting.jsx"));
const BackupsIndex = lazy(() => import("../pages/Backups/Index.jsx"));
const BarcodeIndex = lazy(() => import("../pages/Barcodes/Index.jsx"));
const StockAdjustmentsIndex = lazy(() => import("../pages/stock-adjustments/index.jsx"));
const CreateStockAdjustment = lazy(() => import("../pages/stock-adjustments/Create.jsx"));
const ShowStockAdjustment = lazy(() => import("../pages/stock-adjustments/Show.jsx"));
// Stocktake Sessions
const StocktakeIndex = lazy(() => import("../pages/stocktake/index.jsx"));
const CreateStocktake = lazy(() => import("../pages/stocktake/Create.jsx"));
const StocktakeSession = lazy(() => import("../pages/stocktake/Session.jsx"));
const StocktakeReview = lazy(() => import("../pages/stocktake/Review.jsx"));
// Stock Disposals & Quarantine
const StockDisposalsIndex = lazy(() => import("../pages/StockDisposals/index.jsx"));
const CreateStockDisposal = lazy(() => import("../pages/StockDisposals/Create.jsx"));
// Financial Accounts & Payment Vouchers
const AccountsPage = lazy(() => import("../pages/Accounts/index.jsx"));
const PaymentsPage = lazy(() => import("../pages/Payments/index.jsx"));
// Cash Drawer
const CashDrawerPage = lazy(() => import("../pages/CashDrawer/index.jsx"));
const SupplierLedgerPage = lazy(() => import("../pages/Ledger/SupplierLedgerPage.jsx"));
const CustomerLedgerPage = lazy(() => import("../pages/Ledger/CustomerLedgerPage.jsx"));
const UsersIndex = lazy(() => import("../pages/users/index.jsx"));
const CreateUser = lazy(() => import("../pages/users/Create.jsx"));
const EditUser = lazy(() => import("../pages/users/Edit.jsx"));
const RolesIndex = lazy(() => import("../pages/roles/index.jsx"));
const CreateRole = lazy(() => import("../pages/roles/Create.jsx"));
const EditRole = lazy(() => import("../pages/roles/Edit.jsx"));
const ActivateLicense = lazy(() => import("../pages/ActivateLicense.jsx"));

function RouteFallback() {
  return (
    <div className="flex items-center justify-center min-h-[40vh] text-sm text-slate-500 dark:text-slate-400">
      Loading…
    </div>
  );
}

export default function AppRoutes() {
  return (
    <Suspense fallback={<RouteFallback />}>
      <Routes>
        {/* Public - Login page (redirects to dashboard if already authenticated) */}
        <Route path="/" element={
          <AuthGuard requireAuth={false}>
            <Login />
          </AuthGuard>
        } />

        {/* Public - Forgot Password page */}
        <Route path="/forgot-password" element={
          <AuthGuard requireAuth={false}>
            <ForgotPassword />
          </AuthGuard>
        } />

        {/* Public - Reset Password page */}
        <Route path="/reset-password/:token" element={
          <AuthGuard requireAuth={false}>
            <ResetPassword />
          </AuthGuard>
        } />
        <Route path="/reset-password/:token/:email" element={
          <AuthGuard requireAuth={false}>
            <ResetPassword />
          </AuthGuard>
        } />

         {/* Protected Routes */}
  <Route
    path="/dashboard"
    element={
      <ProtectedRoute>
        <DashboardLayout>
          <Dashboard />
        </DashboardLayout>
      </ProtectedRoute>
    }
  />

  <Route
    path="/profile"
    element={
      <ProtectedRoute>
        <DashboardLayout>
          <Profile />
        </DashboardLayout>
      </ProtectedRoute>
    }
  />
  <Route
    path="/suppliers"
    element={
      <ProtectedRoute>
        <DashboardLayout>
          <Suppliers />
        </DashboardLayout>
      </ProtectedRoute>
    }
  />
  <Route
    path="/customers"
    element={
      <ProtectedRoute>
        <DashboardLayout>
          <Customers />
        </DashboardLayout>
      </ProtectedRoute>
    }
  />

  <Route
    path="/categories"
    element={
      <ProtectedRoute>
        <DashboardLayout>
          <Categories />
        </DashboardLayout>
      </ProtectedRoute>
    }
  />

  <Route
    path="/brands"
    element={
      <ProtectedRoute>
        <DashboardLayout>
          <Brands />
        </DashboardLayout>
      </ProtectedRoute>
    }
  />
  <Route
  path="/products"
  element={
    <ProtectedRoute>
      <DashboardLayout>
        <ProductsIndex />
      </DashboardLayout>
    </ProtectedRoute>
  }
/>

<Route
  path="/products/create"
  element={
    <ProtectedRoute>
      <DashboardLayout>
        <CreateProduct />
      </DashboardLayout>
    </ProtectedRoute>
  }
/>

<Route
  path="/products/:id/edit"
  element={
    <ProtectedRoute>
      <DashboardLayout>
        <EditProduct />
      </DashboardLayout>
    </ProtectedRoute>
  }
/>
      {/* Purchase Invoices */}
      <Route
        path="/purchase-invoices"
        element={
          <ProtectedRoute>
            <DashboardLayout>
              <PurchaseInvoicesIndex />
            </DashboardLayout>
          </ProtectedRoute>
        }
      />
      <Route
        path="/purchase-invoices/create"
        element={
          <ProtectedRoute>
            <DashboardLayout>
              <CreatePurchaseInvoice />
            </DashboardLayout>
          </ProtectedRoute>
        }
      />
      <Route
        path="/purchase-invoices/:id/edit"
        element={
          <ProtectedRoute>
            <DashboardLayout>
              <EditPurchaseInvoice />
            </DashboardLayout>
          </ProtectedRoute>
        }
      />
      <Route
        path="/purchase-invoices/:id"
        element={
          <ProtectedRoute>
            <DashboardLayout>
              <ShowPurchaseInvoice />
            </DashboardLayout>
          </ProtectedRoute>
        }
      />
      {/* Purchase Returns */}
      <Route
        path="/purchase-returns"
        element={
          <ProtectedRoute>
            <DashboardLayout>
              <PurchaseReturnsIndex />
            </DashboardLayout>
          </ProtectedRoute>
        }
      />
      <Route
        path="/purchase-returns/create"
        element={
          <ProtectedRoute>
            <DashboardLayout>
              <CreatePurchaseReturn />
            </DashboardLayout>
          </ProtectedRoute>
        }
      />
      <Route
        path="/purchase-returns/:id/edit"
        element={
          <ProtectedRoute>
            <DashboardLayout>
              <EditPurchaseReturn />
            </DashboardLayout>
          </ProtectedRoute>
        }
      />
      {/* Sale Invoice */}
      <Route 
      path="sale-invoices"
      element={
        <ProtectedRoute>
          <DashboardLayout>
            <IndexSaleInvoice />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      <Route 
      path="sale-invoices/pos"
      element={
        <ProtectedRoute>
          <BarcodePos />
        </ProtectedRoute>
      }
      />
      <Route 
      path="sale-invoices/barcode-pos"
      element={
        <ProtectedRoute>
          <BarcodePos />
        </ProtectedRoute>
      }
      />
      <Route 
      path="sale-invoices/create/retail"
      element={
        <ProtectedRoute>
          <DashboardLayout>
            <CreateSaleInvoiceRetail />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      <Route 
      path="sale-invoices/create-retail"
      element={
        <ProtectedRoute>
          <DashboardLayout>
            <CreateSaleInvoiceRetail />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      <Route 
      path="sale-invoices/create/wholesale"
      element={
        <ProtectedRoute>
          <SaleSystemGuard requiredSaleSystem="wholesale">
            <DashboardLayout>
              <CreateSaleInvoiceWholesale />
            </DashboardLayout>
          </SaleSystemGuard>
        </ProtectedRoute>
      }
      />
      <Route 
      path="sale-invoices/create-wholesale"
      element={
        <ProtectedRoute>
          <SaleSystemGuard requiredSaleSystem="wholesale">
            <DashboardLayout>
              <CreateSaleInvoiceWholesale />
            </DashboardLayout>
          </SaleSystemGuard>
        </ProtectedRoute>
      }
      />
      <Route
      path="sale-invoices/:id/edit"
      element={
        <ProtectedRoute>
          <DashboardLayout>
            <EditSaleInvoice />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      <Route 
      path="sale-invoices/:id"
      element={
        <ProtectedRoute>
          <DashboardLayout>
            <ShowSaleInvoice />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      {/* Sale Returns */}
      <Route
      path="sale-returns"
      element= {
        <ProtectedRoute>
          <DashboardLayout>
            <IndexSaleReturn />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      <Route
      path="sale-returns/create"
      element= {
        <ProtectedRoute>
          <DashboardLayout>
            <CreateSaleReturn />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      <Route
      path="sale-returns/:id/edit"
      element= {
        <ProtectedRoute>
          <DashboardLayout>
            <EditSaleReturn />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
{/* Purchase Orders */}
      <Route
      path="purchase-orders/forecast"
      element= {
        <ProtectedRoute>
          <DashboardLayout>
            <PurchaseOrder />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      {['/purchase-orders', '/purchase-orders/create', '/purchase-orders/:id', '/purchase-orders/:id/edit'].map(path => (
        <Route key={path} path={path} element={<ProtectedRoute><DashboardLayout><PurchaseOrders /></DashboardLayout></ProtectedRoute>} />
      ))}
      {/* User Demands */}
      <Route
      path="user-demands"
      element= {
        <ProtectedRoute>
          <DashboardLayout>
            <UserDemandsIndex />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      <Route
      path="user-demands/create"
      element= {
        <ProtectedRoute>
          <DashboardLayout>
            <CreateUserDemand />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />

      { /* Reports */}
      <Route
      path="reports/cost-of-sale"
      element= {
        <ProtectedRoute>
          <DashboardLayout>
            <CostOfSaleReport />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      <Route
      path="reports/cost-of-sale-detail"
      element= {
        <ProtectedRoute>
          <DashboardLayout>
            <CostOfSaleDetailReport />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      <Route
      path="reports/purchase-detail"
      element= {
        <ProtectedRoute>
          <DashboardLayout>
            <PurchaseDetailReport />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      <Route
      path="reports/sale-detail"
      element= {
        <ProtectedRoute>
          <DashboardLayout>
            <SaleDetailReport />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      <Route
      path="reports/current-stock"
      element= {
        <ProtectedRoute>
          <DashboardLayout>
            <CurrentStockReport />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      <Route
      path="reports/stock-adjustment"
      element= {
        <ProtectedRoute>
          <DashboardLayout>
            <StockAdjustmentReport />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      <Route
      path="reports/product-comprehensive"
      element= {
        <ProtectedRoute>
          <DashboardLayout>
            <ProductComprehensiveReport />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      <Route
      path="reports/near-expiry-product"
      element= {
        <ProtectedRoute>
          <DashboardLayout>
            <NearExpiryProductReport />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      <Route path="reports/purchase-returns" element={<ProtectedRoute><DashboardLayout><MissingReports type="purchase-returns" /></DashboardLayout></ProtectedRoute>} />
      <Route path="reports/sale-returns" element={<ProtectedRoute><DashboardLayout><MissingReports type="sale-returns" /></DashboardLayout></ProtectedRoute>} />
      <Route path="reports/purchase-summary" element={<ProtectedRoute><DashboardLayout><MissingReports type="purchase-summary" /></DashboardLayout></ProtectedRoute>} />
      <Route path="reports/outstanding-aging" element={<ProtectedRoute><DashboardLayout><MissingReports type="outstanding-aging" /></DashboardLayout></ProtectedRoute>} />
      <Route path="reports/reorder" element={<ProtectedRoute><DashboardLayout><MissingReports type="reorder" /></DashboardLayout></ProtectedRoute>} />
      <Route path="reports/net-profit" element={<ProtectedRoute><DashboardLayout><NetProfitReport /></DashboardLayout></ProtectedRoute>} />
      
      {/* Operating Expenses */}
      <Route path="expenses" element={<ProtectedRoute><DashboardLayout><ExpensesIndex /></DashboardLayout></ProtectedRoute>} />
      {/* Batch Expiry & Tracking */}
      <Route
        path="batch-expiry"
        element={
          <ProtectedRoute>
            <DashboardLayout>
              <BatchExpiryTracking />
            </DashboardLayout>
          </ProtectedRoute>
        }
      />
      {/* Settings */}
      <Route
      path="settings"
      element= {
        <ProtectedRoute>
          <DashboardLayout>
            <Setting />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      {/* Backups & Offsite Storage */}
      <Route
      path="backups"
      element= {
        <ProtectedRoute>
          <DashboardLayout>
            <BackupsIndex />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      {/* Barcode Studio & Label Printing */}
      <Route
      path="barcode-labels"
      element= {
        <ProtectedRoute>
          <DashboardLayout>
            <BarcodeIndex />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      {/* Stock Adjustments */}
      <Route
      path="stock-adjustments"
      element= {
        <ProtectedRoute>
          <DashboardLayout>
            <StockAdjustmentsIndex />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      <Route
      path="stock-adjustments/create"
      element= {
        <ProtectedRoute>
          <DashboardLayout>
            <CreateStockAdjustment />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      <Route
      path="stock-adjustments/:id"
      element= {
        <ProtectedRoute>
          <DashboardLayout>
            <ShowStockAdjustment />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      {/* Stocktake Sessions */}
      <Route
      path="stocktake-sessions"
      element={
        <ProtectedRoute>
          <DashboardLayout>
            <StocktakeIndex />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      <Route
      path="stocktake-sessions/create"
      element={
        <ProtectedRoute>
          <DashboardLayout>
            <CreateStocktake />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      <Route
      path="stocktake-sessions/:id"
      element={
        <ProtectedRoute>
          <DashboardLayout>
            <StocktakeSession />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      <Route
      path="stocktake-sessions/:id/review"
      element={
        <ProtectedRoute>
          <DashboardLayout>
            <StocktakeReview />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      {/* Stock Disposals & Quarantine */}
      <Route
      path="stock-disposals"
      element={
        <ProtectedRoute>
          <DashboardLayout>
            <StockDisposalsIndex />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      <Route
      path="stock-disposals/create"
      element={
        <ProtectedRoute>
          <DashboardLayout>
            <CreateStockDisposal />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      {/* Financial Accounts */}
      <Route
      path="/accounts"
      element={
        <ProtectedRoute>
          <DashboardLayout>
            <AccountsPage />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      {/* Payment Vouchers */}
      <Route
      path="/payments"
      element={
        <ProtectedRoute>
          <DashboardLayout>
            <PaymentsPage />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      {/* Cash Drawer */}
      <Route
      path="/cash-drawer"
      element={
        <ProtectedRoute>
          <DashboardLayout>
            <CashDrawerPage />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
            {/* Supplier Ledger */}
      <Route
      path="/supplier-ledger"      element= {
        <ProtectedRoute>
          <DashboardLayout>
            <SupplierLedgerPage />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      {/* Customer Ledger */}
      <Route
      path="/customer-ledger"
      element= {
        <ProtectedRoute>
          <DashboardLayout>
            <CustomerLedgerPage />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      {/* Users */}
      <Route
      path="/users"
      element= {
        <ProtectedRoute>
          <DashboardLayout>
            <UsersIndex />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      <Route
      path="/users/create"
      element= {
        <ProtectedRoute>
          <DashboardLayout>
            <CreateUser />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      <Route
      path="/users/:id/edit"
      element= {
        <ProtectedRoute>
          <DashboardLayout>
            <EditUser />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      {/* Roles */}
      <Route
      path="/roles"
      element= {
        <ProtectedRoute>
          <DashboardLayout>
            <RolesIndex />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      <Route
      path="/roles/create"
      element= {
        <ProtectedRoute>
          <DashboardLayout>
            <CreateRole />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      <Route
      path="/roles/:id/edit"
      element= {
        <ProtectedRoute>
          <DashboardLayout>
            <EditRole />
          </DashboardLayout>
        </ProtectedRoute>
      }
      />
      {/* License Activation - requires auth but not license */}
      <Route path="/activate" element={
        <AuthGuard requireAuth={true}>
          <ActivateLicense />
        </AuthGuard>
      } />

      </Routes>
    </Suspense>
  );
}
