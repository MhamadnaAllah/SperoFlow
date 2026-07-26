"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { usePathname, useRouter } from "next/navigation";

import { ApiError, aiProposalsApi, rolesApi, tasksApi } from "@/lib/api/client";

function titleCase(value) {
  return String(value || "").replace(/([A-Z])/g, " $1").replace(/^./, (character) => character.toUpperCase());
}

function messageFrom(error, fallback) {
  return error instanceof ApiError ? error.message : fallback;
}

function RoleRow({ item, tasks }) {
  const [isOpen, setIsOpen] = useState(false);
  const router = useRouter();
  const visibleTasks = tasks.slice(0, 4);

  return (
    <div className="rounded-lg transition-colors hover:bg-white/70">
      <button
        aria-expanded={isOpen}
        className="flex w-full items-center justify-between rounded-lg px-2.5 py-2 text-left transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
        onClick={() => setIsOpen((current) => !current)}
        type="button"
      >
        <span className="flex min-w-0 items-center gap-2.5">
          <span
            className="material-symbols-outlined flex-shrink-0"
            style={{ color: item.color, fontSize: "17px", fontVariationSettings: "'FILL' 1" }}
          >
            {item.icon}
          </span>
          <span className="truncate text-xs font-semibold text-slate-700">{item.name}</span>
        </span>
        <span className="flex items-center gap-1.5 text-slate-400">
          <span className="rounded-md bg-slate-200/60 px-1.5 py-0.5 text-[10px] font-semibold">{tasks.length}</span>
          <span
            className="material-symbols-outlined transition-transform duration-200"
            style={{ fontSize: "16px", transform: isOpen ? "rotate(180deg)" : "rotate(0deg)" }}
          >
            expand_more
          </span>
        </span>
      </button>

      {isOpen && (
        <div className="mb-1 ml-4 space-y-1 border-l-2 py-1 pl-3" style={{ borderColor: `${item.color}33` }}>
          {visibleTasks.map((task) => (
            <button
              className="block w-full rounded-md border-l-2 bg-white/70 px-2 py-1.5 text-left transition-colors hover:bg-white"
              key={task.id}
              onClick={() => router.push("/tasks")}
              style={{ borderLeftColor: item.color }}
              title="Open Tasks"
              type="button"
            >
              <span className={`block truncate text-[11px] font-semibold ${task.state === "completed" ? "text-slate-400 line-through" : "text-slate-700"}`}>{task.title}</span>
              <span className="mt-0.5 block text-[10px] text-slate-400">{task.estimatedMinutes ? `${task.estimatedMinutes} min` : titleCase(task.lifeArea)}</span>
            </button>
          ))}
          {tasks.length > visibleTasks.length && <p className="px-1 pt-1 text-[10px] font-semibold text-slate-400">+{tasks.length - visibleTasks.length} more</p>}
          {tasks.length === 0 && <p className="px-1 py-1 text-[10px] text-slate-400">No active tasks.</p>}
        </div>
      )}
    </div>
  );
}

function RoleSection({ children, title }) {
  const [isOpen, setIsOpen] = useState(true);

  return (
    <section>
      <button
        aria-expanded={isOpen}
        className="mb-1 flex w-full items-center justify-between px-1 py-1 text-left focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
        onClick={() => setIsOpen((current) => !current)}
        type="button"
      >
        <span className="text-[10px] font-extrabold uppercase tracking-[0.12em] text-slate-400">{title}</span>
        <span
          className="material-symbols-outlined text-slate-400 transition-transform duration-200"
          style={{ fontSize: "15px", transform: isOpen ? "rotate(0deg)" : "rotate(-90deg)" }}
        >
          expand_more
        </span>
      </button>
      {isOpen && <div className="space-y-1">{children}</div>}
    </section>
  );
}

export default function Sidebar({ isCompact = false, isOpen = true, onClose }) {
  const pathname = usePathname();
  const router = useRouter();
  const [roles, setRoles] = useState([]);
  const [tasks, setTasks] = useState([]);
  const [pendingProposals, setPendingProposals] = useState([]);
  const [loading, setLoading] = useState(true);
  const [notice, setNotice] = useState(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const nextRoles = await rolesApi.bootstrap();
      const [nextTasks, nextProposals] = await Promise.all([
        tasksApi.list(),
        aiProposalsApi.list({ state: "pending" }),
      ]);
      setRoles(nextRoles);
      setTasks(nextTasks);
      setPendingProposals(nextProposals);
      setNotice(null);
    } catch (error) {
      setNotice(messageFrom(error, "Unable to load your roles."));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  const activeRoles = useMemo(
    () => roles.filter((role) => !role.isArchived).sort((left, right) => left.sortOrder - right.sortOrder || left.name.localeCompare(right.name)),
    [roles],
  );
  const tasksByRole = useMemo(() => {
    const fallbackByLifeArea = new Map();
    activeRoles.forEach((role) => {
      if (!fallbackByLifeArea.has(role.defaultLifeArea)) fallbackByLifeArea.set(role.defaultLifeArea, role.id);
    });
    const grouped = new Map(activeRoles.map((role) => [role.id, []]));
    tasks
      .filter((task) => task.state !== "cancelled")
      .forEach((task) => {
        const targetRoleId = task.roleId || fallbackByLifeArea.get(task.lifeArea);
        if (targetRoleId && grouped.has(targetRoleId)) grouped.get(targetRoleId).push(task);
      });
    grouped.forEach((roleTasks) => roleTasks.sort((left, right) => (left.state === "completed") - (right.state === "completed") || left.sortOrder - right.sortOrder));
    return grouped;
  }, [activeRoles, tasks]);
  const internalRoles = activeRoles.filter((role) => role.category === "internal");
  const externalRoles = activeRoles.filter((role) => role.category === "external");
  const isRolesPage = pathname === "/roles";

  const navigate = (path) => {
    router.push(path);
    if (isCompact) onClose?.();
  };

  return (
    <aside
      aria-hidden={!isOpen}
      inert={!isOpen}
      aria-label="Life roles sidebar"
      className={`fixed left-0 top-16 z-40 flex h-[calc(100vh-4rem)] w-[min(18rem,calc(100vw-2rem))] flex-col border-r border-black/5 bg-[#f1f4f6]/95 px-3 pb-2 pt-3 shadow-xl shadow-slate-900/5 backdrop-blur-xl transition-transform duration-200 ease-out lg:w-72 lg:shadow-none ${isOpen ? "translate-x-0" : "-translate-x-[calc(100%+1rem)]"}`}
      id="balance-sidebar"
    >
      <div className="mb-3 flex items-center justify-between px-1 lg:hidden">
        <span className="text-sm font-bold text-slate-800">Life roles</span>
        <button
          aria-label="Close life roles sidebar"
          className="flex h-8 w-8 items-center justify-center rounded-lg text-slate-500 hover:bg-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
          onClick={onClose}
          title="Close life roles sidebar"
          type="button"
        >
          <span className="material-symbols-outlined" style={{ fontSize: "18px" }}>close</span>
        </button>
      </div>

      {notice && <div className="mb-3 flex items-start justify-between gap-2 rounded-lg border border-error/20 bg-error/10 px-3 py-2 text-[11px] font-medium text-error"><span>{notice}</span><button aria-label="Dismiss role loading error" className="text-error" onClick={() => setNotice(null)} type="button"><span className="material-symbols-outlined" style={{ fontSize: "15px" }}>close</span></button></div>}

      <div className="sidebar-scroll flex-1 space-y-4 overflow-y-auto pb-3">
        {loading ? <div className="space-y-3 px-1">{Array.from({ length: 6 }).map((_, index) => <div className="h-8 animate-pulse rounded-lg bg-slate-200/70" key={index} />)}</div> : <>
          <RoleSection title="Internal balance">
            {internalRoles.map((role) => <RoleRow item={role} key={role.id} tasks={tasksByRole.get(role.id) || []} />)}
          </RoleSection>
          {externalRoles.length > 0 && <>
            <div className="h-px bg-slate-300/60" />
            <RoleSection title="Role balance">
              {externalRoles.map((role) => <RoleRow item={role} key={role.id} tasks={tasksByRole.get(role.id) || []} />)}
            </RoleSection>
          </>}
        </>}
      </div>

      <div className="space-y-1 border-t border-slate-300/60 pt-2">
        <button
          className={`flex w-full items-center gap-3 rounded-lg px-3 py-2 text-left text-sm font-semibold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 ${pathname.startsWith("/coach") ? "bg-primary/10 text-primary" : "text-slate-500 hover:bg-white/70 hover:text-slate-800"}`}
          onClick={() => navigate("/coach")}
          type="button"
        >
          <span className="material-symbols-outlined" style={{ fontSize: "20px" }}>psychology</span>
          Personal Coach
        </button>
        <button
          className={`flex w-full items-center gap-3 rounded-lg px-3 py-2 text-left text-sm font-semibold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 ${isRolesPage ? "bg-primary/10 text-primary" : "text-slate-500 hover:bg-white/70 hover:text-slate-800"}`}
          onClick={() => navigate("/roles")}
          type="button"
        >
          <span className="material-symbols-outlined" style={{ fontSize: "20px" }}>account_tree</span>
          Manage roles
        </button>
        <button
          className="flex w-full items-center justify-between rounded-lg px-3 py-2 text-left text-sm font-semibold text-slate-500 transition-colors hover:bg-white/70 hover:text-slate-800 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
          onClick={() => navigate("/roles?view=suggestions")}
          type="button"
        >
          <span className="flex items-center gap-3"><span className="material-symbols-outlined" style={{ fontSize: "20px" }}>auto_awesome</span>Suggestions</span>
          {pendingProposals.length > 0 && <span className="rounded-md bg-primary px-1.5 py-0.5 text-[10px] font-bold text-on-primary">{pendingProposals.length}</span>}
        </button>
      </div>
    </aside>
  );
}