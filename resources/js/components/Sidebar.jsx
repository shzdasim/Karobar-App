// src/components/Sidebar.jsx
import React from "react";
import ClassicSidebar from "./sidebar-templates/ClassicSidebar.jsx";

// The app uses a single fixed sidebar template
// (settings no longer offer sidebar/topbar template choices)
export default function Sidebar(props) {
  return <ClassicSidebar {...props} />;
}
