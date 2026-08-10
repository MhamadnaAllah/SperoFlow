"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { usePathname, useRouter } from "next/navigation";
import { useDraggable, useDroppable } from "@dnd-kit/core";
import { CSS } from "@dnd-kit/utilities";

import { ApiError, aiProposalsApi, rolesApi, tasksApi } from "@/lib/api/client";

function titleCase(value) {
  return String(value || "").replace(/([A-Z])/g, " $1").replace(/^./, (character) => character.toUpperCase());
}

function messageFrom(error, fallback) {
  return error instanceof ApiError ? error.message : fallback;
}

const ENERGY_COLORS = {
  High: { bg: "rgba(239,68,68,0.08)", text: "#dc2626" },
  Medium: { bg: "rgba(245,158,11,0.08)", text: "#b45309" },
  Low: { bg: "rgba(16,185,129,0.08)", text: "#059669" },
};

// ─── Draggable Task Card for Sidebar ─────────────────────────────────────────
function SidebarTaskCard({ task, accentColor }) {
  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({
    id: `sidebar-${task.id}`,
    data: { type: "sidebar-task", title: task.title, tag: task.quadrant || "Medium", ...task },
  });

  const router = useRouter();
  const style = {
    transform: CSS.Translate.toString(transform),
    opacity: isDragging ? 0.4 : 1,
    borderLeftColor: accentColor,
  };

  const energyLevel = task.estimatedMinutes > 60 ? "High" : task.estimatedMinutes > 30 ? "Medium" : "Low";
  const ec = ENERGY_COLORS[energyLevel] || ENERGY_COLORS.Low;

  return (
    <div
      ref={setNodeRef}
      style={style}
      {...listeners}
      {...attributes}
      className="sidebar-task-card group relative flex cursor-grab flex-col rounded-lg border-l-2 bg-white/80 p-2.5 shadow-sm transition-all hover:bg-white hover:shadow-md"
      onClick={() => router.push("/tasks")}
    >
      <div className="flex items-start justify-between gap-2">
        <span className={`text-xs font-semibold leading-tight ${task.state === "completed" ? "line-through text-slate-400" : "text-slate-800"}`}>
          {task.title}
        </span>
        <span className="material-symbols-outlined flex-shrink-0 text-slate-300 group-hover:text-slate-500" style={{ fontSize: "14px", marginTop: "1px" }}>
          drag_indicator
        </span>
      </div>
      <div className="mt-1.5 flex items-center gap-2">
        <span className="text-[10px] font-medium text-slate-400">
          ⏱ {task.estimatedMinutes ? `${task.estimatedMinutes}m` : "15m"}
        </span>
        <span
          className="rounded-md px-1.5 py-0.5 text-[9px] font-bold"
          style={{ background: ec.bg, color: ec.text }}
        >
          {energyLevel}
        </span>
      </div>
    </div>
  );
}

// ─── Expandable Role Row ───────────────────────────────────────────────────────
function ExpandableRoleRow({ role, tasks, accentColor, icon }) {
  const [open, setOpen] = useState(false);
  const router = useRouter();
  const { isOver, setNodeRef } = useDroppable({
    id: `sidebar-folder-${role.id}`,
    data: { type: "sidebar-folder", folderId: role.id },
  });

  return (
    <div ref={setNodeRef} className={isOver ? "rounded-xl bg-primary/5 ring-2 ring-primary/50" : ""}>
      <div
        className="flex cursor-pointer items-center justify-between rounded-xl p-2.5 transition-all hover:bg-white/70"
        style={{ background: open ? "rgba(255,255,255,0.7)" : "rgba(255,255,255,0.3)" }}
        onClick={() => setOpen((o) => !o)}
      >
        <div className="flex items-center gap-2.5">
          {icon ? (
            <span
              className="material-symbols-outlined text-[16px]"
              style={{ color: accentColor, fontVariationSettings: "'FILL' 1" }}
            >
              {icon}
            </span>
          ) : (
            <div className="h-2 w-2 flex-shrink-0 rounded-full" style={{ background: accentColor }} />
          )}
          <span className="text-xs font-semibold text-slate-700">{role.name}</span>
        </div>
        <div className="flex items-center gap-1">
          <span className="rounded-md bg-slate-400/10 px-1.5 py-0.5 text-[10px] font-semibold text-slate-400">
            {tasks.length}
          </span>
          <span
            className="material-symbols-outlined text-[16px] text-slate-400 transition-transform duration-250"
            style={{ transform: open ? "rotate(180deg)" : "rotate(0deg)" }}
          >
            expand_more
          </span>
        </div>
      </div>
      <div
        style={{
          overflow: "hidden",
          transition: "max-height 0.3s ease, opacity 0.2s ease",
          maxHeight: open ? "600px" : "0",
          opacity: open ? 1 : 0,
        }}
      >
        <div className="my-1 ml-3 space-y-1.5 border-l-2 py-1 pl-3" style={{ borderColor: `${accentColor}22` }}>
          {tasks.map((task) => (
            <SidebarTaskCard key={task.id} task={task} accentColor={accentColor} />
          ))}
          <button
            className="flex w-full items-center gap-1 border-none bg-none py-1.5 text-[10px] font-semibold text-slate-400 transition-colors hover:text-primary"
            onClick={() => router.push("/tasks")}
            type="button"
          >
            <span className="material-symbols-outlined text-[14px]">add</span>
            Add task
          </button>
        </div>
      </div>
    </div>
  );
}

// ─── Collapsible Sidebar Section ──────────────────────────────────────────────
function SidebarSection({ title, children }) {
  const [open, setOpen] = useState(true);

  return (
    <div className="flex flex-col gap-1">
      <div
        className="group mb-1 flex cursor-pointer items-center justify-between px-1"
        onClick={() => setOpen((o) => !o)}
      >
        <span className="text-[10px] font-extrabold uppercase tracking-[0.12em] text-slate-400">
          {title}
        </span>
        <span
          className="material-symbols-outlined text-[15px] text-slate-400 transition-transform duration-250"
          style={{ transform: open ? "rotate(0deg)" : "rotate(-90deg)" }}
        >
          expand_more
        </span>
      </div>
      <div
        style={{
          overflow: "hidden",
          transition: "max-height 0.35s ease, opacity 0.2s",
          maxHeight: open ? "2000px" : "0",
          opacity: open ? 1 : 0,
        }}
      >
        {children}
      </div>
    </div>
  );
}

// ─── Main Sidebar Component ───────────────────────────────────────────────────
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

  useEffect(() => {
    void load();
  }, [load]);

  const activeRoles = useMemo(
    () => roles.filter((role) => !role.isArchived).sort((left, right) => left.sortOrder - right.sortOrder || left.name.localeCompare(right.name)),
    [roles]
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
    grouped.forEach((roleTasks) =>
      roleTasks.sort((left, right) => (left.state === "completed") - (right.state === "completed") || left.sortOrder - right.sortOrder)
    );
    return grouped;
  }, [activeRoles, tasks]);

  const internalRoles = activeRoles.filter((role) => role.category === "internal");
  const externalRoles = activeRoles.filter((role) => role.category === "external");
  const isSettings = pathname === "/settings";

  const navigate = (path) => {
    router.push(path);
    if (isCompact) onClose?.();
  };

  return (
    <aside
      aria-hidden={!isOpen}
      inert={!isOpen}
      aria-label="Life roles sidebar"
      className={`fixed left-0 top-16 z-40 flex h-[calc(100vh-4rem)] w-[min(18rem,calc(100vw-2rem))] flex-col border-r border-black/5 bg-[#f1f4f6]/95 px-3 pb-2 pt-3 shadow-xl shadow-slate-900/5 backdrop-blur-xl transition-transform duration-200 ease-out lg:w-72 lg:shadow-none ${
        isOpen ? "translate-x-0" : "-translate-x-[calc(100%+1rem)]"
      }`}
      id="balance-sidebar"
    >
      {/* Mobile close button */}
      <div className="mb-3 flex items-center justify-between px-1 lg:hidden">
        <span className="text-sm font-bold text-slate-800">Life roles</span>
        <button
          aria-label="Close life roles sidebar"
          className="flex h-8 w-8 items-center justify-center rounded-lg text-slate-500 hover:bg-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
          onClick={onClose}
          type="button"
        >
          <span className="material-symbols-outlined" style={{ fontSize: "18px" }}>
            close
          </span>
        </button>
      </div>

      {notice && (
        <div className="mb-3 flex items-start justify-between gap-2 rounded-lg border border-error/20 bg-error/10 px-3 py-2 text-[11px] font-medium text-error">
          <span>{notice}</span>
          <button aria-label="Dismiss role loading error" className="text-error" onClick={() => setNotice(null)} type="button">
            <span className="material-symbols-outlined" style={{ fontSize: "15px" }}>
              close
            </span>
          </button>
        </div>
      )}

      {/* Scrollable content area */}
      <div className="sidebar-scroll flex-1 space-y-4 overflow-y-auto pb-3">
        {loading ? (
          <div className="space-y-3 px-1">
            {Array.from({ length: 6 }).map((_, index) => (
              <div className="h-8 animate-pulse rounded-lg bg-slate-200/70" key={index} />
            ))}
          </div>
        ) : (
          <>
            {/* INTERNAL BALANCE */}
            <SidebarSection title="Internal Balance">
              {internalRoles.map((role) => (
                <ExpandableRoleRow
                  key={role.id}
                  role={role}
                  accentColor={role.color || "#0053dc"}
                  icon={role.icon}
                  tasks={tasksByRole.get(role.id) || []}
                />
              ))}
            </SidebarSection>

            <div className="my-2 h-px bg-slate-300/50" />

            {/* ROLE BALANCE */}
            <SidebarSection title="Role Balance">
              {externalRoles.map((role) => (
                <ExpandableRoleRow
                  key={role.id}
                  role={role}
                  accentColor={role.color || "#7c3aed"}
                  icon={role.icon || "computer"}
                  tasks={tasksByRole.get(role.id) || []}
                />
              ))}

              {/* AI Suggested Card */}
              {pendingProposals.length > 0 && (
                <div className="mt-2 rounded-xl border-2 border-dashed border-indigo-400/30 bg-gradient-to-br from-indigo-50/70 to-purple-50/70 p-3 shadow-sm">
                  <div className="mb-2 flex items-center justify-between">
                    <span className="rounded-full bg-indigo-600 px-2 py-0.5 text-[9px] font-extrabold uppercase tracking-widest text-white">
                      ✨ AI Suggested
                    </span>
                    <span className="material-symbols-outlined text-[15px] text-indigo-400">auto_awesome</span>
                  </div>
                  <span className="mb-2 block text-xs font-bold text-indigo-950">
                    {pendingProposals[0].title || "Freelancer"}
                  </span>
                  <div className="flex items-center gap-2">
                    <button
                      className="border-none bg-none text-[10px] font-bold text-slate-500 hover:text-slate-800"
                      onClick={() => navigate("/roles?view=suggestions")}
                      type="button"
                    >
                      Why?
                    </button>
                    <button
                      className="flex-1 rounded-lg border-none bg-indigo-600 py-1 text-[10px] font-bold text-white transition-colors hover:bg-indigo-700"
                      onClick={() => navigate("/roles?view=suggestions")}
                      type="button"
                    >
                      Confirm
                    </button>
                  </div>
                </div>
              )}
            </SidebarSection>

            {/* Add a role button */}
            <button
              className="flex w-full items-center justify-center gap-1.5 rounded-xl border-2 border-dashed border-slate-300/60 p-2.5 text-xs font-semibold text-slate-400 transition-all hover:border-slate-400 hover:text-slate-600"
              onClick={() => navigate("/roles")}
              type="button"
            >
              <span className="material-symbols-outlined text-[18px]">add</span>
              Add a role
            </button>
          </>
        )}
      </div>

      {/* Pinned bottom: Settings */}
      <div className="flex-shrink-0 border-t border-slate-300/60 pt-2">
        <button
          className={`flex w-full items-center gap-3 rounded-xl px-3 py-2.5 text-left text-sm font-semibold transition-all ${
            isSettings ? "bg-primary/10 text-primary font-bold" : "text-slate-500 hover:bg-slate-200/50 hover:text-slate-800"
          }`}
          id="sidebar-settings-btn"
          onClick={() => navigate("/settings")}
          type="button"
        >
          <span
            className="material-symbols-outlined text-[20px]"
            style={{ fontVariationSettings: isSettings ? "'FILL' 1" : "'FILL' 0" }}
          >
            settings
          </span>
          Settings
          {isSettings && <span className="ml-auto h-1.5 w-1.5 rounded-full bg-primary" />}
        </button>
      </div>
    </aside>
  );
}