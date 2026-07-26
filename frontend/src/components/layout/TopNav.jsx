"use client";

import { useEffect, useRef, useState } from "react";
import { usePathname, useRouter } from "next/navigation";

import { authApi } from "@/lib/api/client";

const NAV_ITEMS = [
  { id: "coach", path: "/coach", icon: "psychology", label: "Coach" },
  { id: "journaling", path: "/journaling", icon: "menu_book", label: "Journaling" },
  { id: "matrix", path: "/matrix", icon: "grid_view", label: "Matrix" },
  { id: "habits", path: "/habits", icon: "rebase_edit", label: "Habits" },
  { id: "tasks", path: "/tasks", icon: "checklist", label: "Tasks" },
  { id: "projects", path: "/projects", icon: "folder_open", label: "Projects" },
  { id: "calendar", path: "/calendar", icon: "calendar_month", label: "Calendar" },
  { id: "goals", path: "/goals", icon: "ads_click", label: "Goals" },
];

export default function TopNav({ user, onSidebarToggle, sidebarOpen = false }) {
  const router = useRouter();
  const pathname = usePathname();
  const [showProfileMenu, setShowProfileMenu] = useState(false);
  const menuRef = useRef(null);

  const isActive = (path) => pathname.startsWith(path);

  useEffect(() => {
    const handleClickOutside = (event) => {
      if (menuRef.current && !menuRef.current.contains(event.target)) {
        setShowProfileMenu(false);
      }
    };

    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  const userInitial = user?.email ? user.email.charAt(0).toUpperCase() : "U";
  const sidebarLabel = sidebarOpen ? "Hide Balance sidebar" : "Show Balance sidebar";
  const handleLogout = async () => {
    try {
      await authApi.logout();
    } finally {
      router.replace("/login");
      router.refresh();
    }
  };

  return (
    <header className="fixed top-0 z-50 flex h-16 w-full items-center bg-white/90 px-3 shadow-[0_1px_0_0_rgba(0,0,0,0.06)] backdrop-blur-xl sm:px-5 lg:px-6">
      <div
        className="mr-2 hidden flex-shrink-0 cursor-pointer items-center gap-2 min-[480px]:flex sm:mr-3"
        onClick={() => router.push("/calendar")}
        role="button"
        tabIndex={0}
        onKeyDown={(event) => {
          if (event.key === "Enter" || event.key === " ") {
            event.preventDefault();
            router.push("/calendar");
          }
        }}
      >
        <div
          className="flex h-7 w-7 items-center justify-center rounded-lg shadow-sm"
          style={{ background: "linear-gradient(135deg,#0053dc,#3b82f6)" }}
        >
          <span
            className="material-symbols-outlined text-white"
            style={{ fontSize: "16px", fontVariationSettings: "'FILL' 1" }}
          >
            calendar_month
          </span>
        </div>
        <span className="hidden text-[17px] font-extrabold text-primary min-[1180px]:inline">
          SperoFlow
        </span>
      </div>

      {onSidebarToggle && (
        <button
          aria-controls="balance-sidebar"
          aria-expanded={sidebarOpen}
          aria-label={sidebarLabel}
          className="flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-lg text-slate-500 transition-colors hover:bg-slate-100 hover:text-slate-800 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
          onClick={onSidebarToggle}
          title={sidebarLabel}
          type="button"
        >
          <span className="material-symbols-outlined" style={{ fontSize: "20px" }}>
            {sidebarOpen ? "menu_open" : "menu"}
          </span>
        </button>
      )}

      <nav
        aria-label="Primary navigation"
        className="absolute left-1/2 flex -translate-x-1/2 items-center gap-0 min-[480px]:gap-1"
      >
        {NAV_ITEMS.map((item) => {
          const active = isActive(item.path);
          return (
            <button
              aria-label={item.label}
              aria-current={active ? "page" : undefined}
              className={`flex h-8 w-8 items-center justify-center rounded-lg border-none font-sans transition-all min-[480px]:h-9 min-[480px]:w-9 min-[900px]:h-auto min-[900px]:w-auto min-[900px]:flex-col min-[900px]:gap-0.5 min-[900px]:px-3 min-[900px]:py-1.5 min-[900px]:text-left ${
                active
                  ? "bg-primary/10 text-primary"
                  : "bg-transparent text-slate-500 hover:bg-slate-100 hover:text-slate-800"
              }`}
              key={item.id}
              onClick={() => router.push(item.path)}
              title={item.label}
              type="button"
            >
              <span
                className="material-symbols-outlined text-[18px] min-[480px]:text-[20px]"
                style={{
                  transition: "font-variation-settings 0.2s",
                  ...(active ? { fontVariationSettings: "'FILL' 1,'wght' 500,'GRAD' 0,'opsz' 24" } : {}),
                }}
              >
                {item.icon}
              </span>
              <span className="hidden whitespace-nowrap text-[10px] font-bold min-[900px]:block">
                {item.label}
              </span>
            </button>
          );
        })}
      </nav>

      <div
        className="relative ml-auto flex flex-shrink-0 items-center gap-1 border-l border-slate-100 pl-2 sm:gap-4 sm:pl-4"
        ref={menuRef}
      >
        <button
          aria-expanded={showProfileMenu}
          aria-haspopup="menu"
          className="flex items-center gap-2 rounded-full border-none bg-transparent p-1 pr-1 transition-colors hover:bg-slate-100 sm:pr-2"
          onClick={() => setShowProfileMenu((current) => !current)}
          type="button"
        >
          <div className="flex h-8 w-8 items-center justify-center rounded-full bg-gradient-to-tr from-indigo-500 to-purple-500 text-sm font-bold text-white shadow-sm ring-2 ring-primary/10">
            {userInitial}
          </div>
          <span className="material-symbols-outlined hidden text-slate-400 sm:inline" style={{ fontSize: "18px" }}>
            expand_more
          </span>
        </button>

        {showProfileMenu && (
          <div
            className="absolute right-0 top-12 z-50 mt-2 w-56 rounded-lg border border-slate-100 bg-white py-1 shadow-lg"
            role="menu"
          >
            <div className="border-b border-slate-50 px-4 py-3">
              <p className="truncate text-xs font-semibold text-slate-800">Account</p>
              <p className="mt-0.5 truncate text-xs text-slate-500">{user?.email}</p>
            </div>
            <button
              className="flex w-full items-center gap-2 border-none bg-transparent px-4 py-2 text-left text-sm text-slate-600 hover:bg-slate-50 hover:text-slate-900"
              onClick={() => router.push("/settings")}
              role="menuitem"
              type="button"
            >
              <span className="material-symbols-outlined" style={{ fontSize: "18px" }}>
                settings
              </span>
              Settings
            </button>
            <button
              className="flex w-full items-center gap-2 border-none bg-transparent px-4 py-2 text-left text-sm text-rose-600 hover:bg-rose-50"
              onClick={handleLogout}
              role="menuitem"
              type="button"
            >
              <span className="material-symbols-outlined" style={{ fontSize: "18px" }}>
                logout
              </span>
              Sign Out
            </button>
          </div>
        )}
      </div>
    </header>
  );
}

