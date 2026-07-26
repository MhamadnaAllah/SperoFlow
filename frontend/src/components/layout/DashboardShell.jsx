"use client";

import { useEffect, useState } from "react";

import Sidebar from "@/components/layout/Sidebar";
import TopNav from "@/components/layout/TopNav";

const SIDEBAR_PREFERENCE_KEY = "speroflow-balance-sidebar-open";

export default function DashboardShell({ children, user }) {
  const [isSidebarOpen, setIsSidebarOpen] = useState(true);
  const [isCompactViewport, setIsCompactViewport] = useState(false);

  useEffect(() => {
    const mediaQuery = window.matchMedia("(max-width: 1023px)");

    const syncViewport = () => {
      const compact = mediaQuery.matches;
      setIsCompactViewport(compact);

      if (compact) {
        setIsSidebarOpen(false);
        return;
      }

      setIsSidebarOpen(
        window.localStorage.getItem(SIDEBAR_PREFERENCE_KEY) !== "false"
      );
    };

    syncViewport();
    mediaQuery.addEventListener("change", syncViewport);
    return () => mediaQuery.removeEventListener("change", syncViewport);
  }, []);

  useEffect(() => {
    const handleEscape = (event) => {
      if (event.key === "Escape" && isCompactViewport && isSidebarOpen) {
        setIsSidebarOpen(false);
      }
    };

    document.addEventListener("keydown", handleEscape);
    return () => document.removeEventListener("keydown", handleEscape);
  }, [isCompactViewport, isSidebarOpen]);

  const setSidebarVisibility = (nextOpen) => {
    setIsSidebarOpen(nextOpen);
    if (!isCompactViewport) {
      window.localStorage.setItem(SIDEBAR_PREFERENCE_KEY, String(nextOpen));
    }
  };

  return (
    <div className="min-h-screen bg-surface">
      <TopNav
        user={user}
        onSidebarToggle={() => setSidebarVisibility(!isSidebarOpen)}
        sidebarOpen={isSidebarOpen}
      />

      <Sidebar
        isCompact={isCompactViewport}
        isOpen={isSidebarOpen}
        onClose={() => setSidebarVisibility(false)}
      />

      {isSidebarOpen && (
        <button
          aria-label="Close Balance sidebar"
          className="fixed inset-0 top-16 z-30 bg-slate-950/30 backdrop-blur-[1px] lg:hidden"
          onClick={() => setSidebarVisibility(false)}
          type="button"
        />
      )}

      <main
        className={`h-screen overflow-x-hidden overflow-y-auto bg-surface pt-16 transition-[padding] duration-200 ease-out view-enter ${
          isSidebarOpen ? "lg:pl-72" : "lg:pl-0"
        }`}
        id="main-content"
      >
        {children}
      </main>
    </div>
  );
}

