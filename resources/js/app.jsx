import React from "react";
import ReactDOM from "react-dom/client";
import { BrowserRouter } from "react-router-dom";

import { AuthProvider, useAuth } from "./context/AuthContext.jsx";
import { LicenseProvider } from "./context/LicenseContext.jsx";
import { ThemeProvider } from "./context/ThemeContext.jsx";
import { SaleSystemProvider } from "./context/SaleSystemContext.jsx";
import Routing from "./routes/index.jsx";
import Mascot from "./components/Mascot.jsx";

import { initAxiosAuth } from "./context/axiosSetup.js";
initAxiosAuth();

function MascotWrapper() {
  // Mascot is only available to logged-in users (needs a session token + user)
  const { user, token } = useAuth();

  if (!token || !user) {
    return null;
  }

  return <Mascot />;
}

ReactDOM.createRoot(document.getElementById("app")).render(
  <BrowserRouter>
    <AuthProvider>
      <LicenseProvider>
        <ThemeProvider>
          <SaleSystemProvider>
            <Routing />
            <MascotWrapper />
          </SaleSystemProvider>
        </ThemeProvider>
      </LicenseProvider>
    </AuthProvider>
  </BrowserRouter>
);
