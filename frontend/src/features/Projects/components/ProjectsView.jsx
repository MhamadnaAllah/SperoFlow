"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { DndContext, PointerSensor, useDroppable, useSensor, useSensors } from "@dnd-kit/core";
import { SortableContext, verticalListSortingStrategy, useSortable } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";

import { ApiError, projectsApi, tasksApi } from "@/lib/api/client";

const PROJECT_STATES = ["active", "completed", "archived"];
const PROJECT_COLORS = {
  indigo: "#0053dc",
  emerald: "#007a55",
  rose: "#c8355b",
  amber: "#b56800",
  teal: "#007a78",
};
const TASK_STATES = [
  { id: "todo", label: "To do", color: "#64748b", icon: "radio_button_unchecked" },
  { id: "inProgress", label: "In progress", color: "#0053dc", icon: "play_circle" },
  { id: "completed", label: "Completed", color: "#006d4a", icon: "check_circle" },
  { id: "cancelled", label: "Cancelled", color: "#ac3434", icon: "cancel" },
];
const LIFE_AREAS = ["work", "family", "physical", "spiritual", "social", "learning", "personal"];
const VIEWS = [
  { id: "list", label: "List", icon: "view_list" },
  { id: "board", label: "Board", icon: "view_kanban" },
  { id: "calendar", label: "Calendar", icon: "calendar_month" },
  { id: "timeline", label: "Timeline", icon: "timeline" },
];

function projectColor(value) {
  return PROJECT_COLORS[value] || PROJECT_COLORS.indigo;
}

function titleCase(value) {
  return String(value || "").replace(/([A-Z])/g, " $1").replace(/^./, (character) => character.toUpperCase());
}

function inputDate(value) {
  return value ? new Date(value).toISOString().slice(0, 10) : "";
}

function dateAt(value, hour = 9) {
  return value ? `${value}T${String(hour).padStart(2, "0")}:00:00.000Z` : null;
}

function shortDate(value) {
  return value ? new Date(value).toLocaleDateString(undefined, { month: "short", day: "numeric" }) : "No date";
}

function dayKey(value) {
  return value ? new Date(value).toISOString().slice(0, 10) : null;
}

function messageFrom(error, fallback) {
  return error instanceof ApiError ? error.message : fallback;
}

function IconButton({ icon, label, onClick, tone = "default", disabled = false }) {
  const toneClass = tone === "danger" ? "text-error hover:bg-error/10" : "text-on-surface-variant hover:bg-surface-container";
  return (
    <button aria-label={label} className={`flex h-8 w-8 items-center justify-center rounded-lg transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 ${toneClass}`} disabled={disabled} onClick={onClick} title={label} type="button">
      <span className="material-symbols-outlined" style={{ fontSize: "18px" }}>{icon}</span>
    </button>
  );
}

function Toast({ notice, onDismiss }) {
  if (!notice) return null;
  const isError = notice.type === "error";
  return (
    <div className={`fixed right-4 top-20 z-[70] flex max-w-sm items-start gap-3 rounded-lg border px-4 py-3 shadow-xl ${isError ? "border-error/20 bg-white text-error" : "border-secondary/20 bg-white text-secondary"}`} role="status">
      <span className="material-symbols-outlined mt-0.5" style={{ fontSize: "18px" }}>{isError ? "error" : "check_circle"}</span>
      <p className="flex-1 text-sm font-semibold">{notice.message}</p>
      <IconButton icon="close" label="Dismiss message" onClick={onDismiss} />
    </div>
  );
}

function Modal({ children, onClose, title }) {
  return (
    <div className="fixed inset-0 z-[60] flex items-center justify-center bg-slate-950/35 p-4" onMouseDown={onClose} role="presentation">
      <section aria-modal="true" className="max-h-[calc(100vh-2rem)] w-full max-w-lg overflow-y-auto rounded-lg border border-outline-variant/30 bg-white shadow-2xl" onMouseDown={(event) => event.stopPropagation()} role="dialog">
        <header className="flex items-center justify-between border-b border-outline-variant/20 px-5 py-4">
          <h2 className="text-lg font-bold text-on-surface">{title}</h2>
          <IconButton icon="close" label="Close" onClick={onClose} />
        </header>
        {children}
      </section>
    </div>
  );
}

function ProjectForm({ onCancel, onSave, project = null, saving }) {
  const [form, setForm] = useState(() => ({
    name: project?.name || "",
    description: project?.description || "",
    color: project?.color || "indigo",
    icon: project?.icon || "folder",
    startAt: inputDate(project?.startAt),
    targetAt: inputDate(project?.targetAt),
  }));
  const update = (key, value) => setForm((current) => ({ ...current, [key]: value }));

  const submit = (event) => {
    event.preventDefault();
    if (!form.name.trim()) return;
    onSave({
      name: form.name.trim(),
      description: form.description.trim() || null,
      color: form.color,
      icon: form.icon.trim() || "folder",
      startAt: dateAt(form.startAt),
      targetAt: dateAt(form.targetAt, 17),
    });
  };

  return (
    <form className="space-y-4 p-5" onSubmit={submit}>
      <label className="block text-sm font-semibold text-on-surface">
        Name
        <input autoFocus className="mt-1.5 w-full rounded-lg border border-outline-variant/40 bg-surface px-3 py-2.5 text-sm font-normal focus:border-primary/50 focus:outline-none focus:ring-2 focus:ring-primary/20" onChange={(event) => update("name", event.target.value)} value={form.name} />
      </label>
      <label className="block text-sm font-semibold text-on-surface">
        Description
        <textarea className="mt-1.5 min-h-24 w-full resize-y rounded-lg border border-outline-variant/40 bg-surface px-3 py-2.5 text-sm font-normal focus:border-primary/50 focus:outline-none focus:ring-2 focus:ring-primary/20" onChange={(event) => update("description", event.target.value)} value={form.description} />
      </label>
      <div className="grid gap-4 sm:grid-cols-2">
        <label className="block text-sm font-semibold text-on-surface">
          Accent
          <select className="mt-1.5 w-full rounded-lg border border-outline-variant/40 bg-surface px-3 py-2.5 text-sm font-normal" onChange={(event) => update("color", event.target.value)} value={form.color}>
            <option value="indigo">Indigo</option><option value="emerald">Emerald</option><option value="rose">Rose</option><option value="amber">Amber</option><option value="teal">Teal</option>
          </select>
        </label>
        <label className="block text-sm font-semibold text-on-surface">
          Icon
          <input className="mt-1.5 w-full rounded-lg border border-outline-variant/40 bg-surface px-3 py-2.5 text-sm font-normal" onChange={(event) => update("icon", event.target.value)} value={form.icon} />
        </label>
      </div>
      <div className="grid gap-4 sm:grid-cols-2">
        <label className="block text-sm font-semibold text-on-surface">Start date<input className="mt-1.5 w-full rounded-lg border border-outline-variant/40 bg-surface px-3 py-2.5 text-sm font-normal" onChange={(event) => update("startAt", event.target.value)} type="date" value={form.startAt} /></label>
        <label className="block text-sm font-semibold text-on-surface">Target date<input className="mt-1.5 w-full rounded-lg border border-outline-variant/40 bg-surface px-3 py-2.5 text-sm font-normal" onChange={(event) => update("targetAt", event.target.value)} type="date" value={form.targetAt} /></label>
      </div>
      <footer className="flex justify-end gap-3 border-t border-outline-variant/20 pt-4">
        <button className="rounded-lg px-4 py-2 text-sm font-semibold text-on-surface-variant hover:bg-surface-container" onClick={onCancel} type="button">Cancel</button>
        <button className="rounded-lg bg-primary px-4 py-2 text-sm font-bold text-on-primary disabled:opacity-50" disabled={saving || !form.name.trim()} type="submit">{saving ? "Saving" : project ? "Save project" : "Create project"}</button>
      </footer>
    </form>
  );
}

function TaskForm({ onCancel, onSave, saving }) {
  const [form, setForm] = useState({ title: "", description: "", lifeArea: "work", state: "todo", startAt: "", dueAt: "", estimatedMinutes: "" });
  const update = (key, value) => setForm((current) => ({ ...current, [key]: value }));
  return (
    <form className="space-y-4 p-5" onSubmit={(event) => {
      event.preventDefault();
      if (!form.title.trim()) return;
      onSave({
        title: form.title.trim(), description: form.description.trim() || null, lifeArea: form.lifeArea, state: form.state,
        startAt: dateAt(form.startAt), dueAt: dateAt(form.dueAt, 17), estimatedMinutes: form.estimatedMinutes ? Number(form.estimatedMinutes) : null,
      });
    }}>
      <label className="block text-sm font-semibold text-on-surface">Task<input autoFocus className="mt-1.5 w-full rounded-lg border border-outline-variant/40 bg-surface px-3 py-2.5 text-sm font-normal" onChange={(event) => update("title", event.target.value)} value={form.title} /></label>
      <label className="block text-sm font-semibold text-on-surface">Details<textarea className="mt-1.5 min-h-20 w-full resize-y rounded-lg border border-outline-variant/40 bg-surface px-3 py-2.5 text-sm font-normal" onChange={(event) => update("description", event.target.value)} value={form.description} /></label>
      <div className="grid gap-4 sm:grid-cols-2">
        <label className="block text-sm font-semibold text-on-surface">Life area<select className="mt-1.5 w-full rounded-lg border border-outline-variant/40 bg-surface px-3 py-2.5 text-sm font-normal" onChange={(event) => update("lifeArea", event.target.value)} value={form.lifeArea}>{LIFE_AREAS.map((area) => <option key={area} value={area}>{titleCase(area)}</option>)}</select></label>
        <label className="block text-sm font-semibold text-on-surface">Status<select className="mt-1.5 w-full rounded-lg border border-outline-variant/40 bg-surface px-3 py-2.5 text-sm font-normal" onChange={(event) => update("state", event.target.value)} value={form.state}>{TASK_STATES.map((state) => <option key={state.id} value={state.id}>{state.label}</option>)}</select></label>
      </div>
      <div className="grid gap-4 sm:grid-cols-3">
        <label className="block text-sm font-semibold text-on-surface">Start<input className="mt-1.5 w-full rounded-lg border border-outline-variant/40 bg-surface px-3 py-2.5 text-sm font-normal" onChange={(event) => update("startAt", event.target.value)} type="date" value={form.startAt} /></label>
        <label className="block text-sm font-semibold text-on-surface">Due<input className="mt-1.5 w-full rounded-lg border border-outline-variant/40 bg-surface px-3 py-2.5 text-sm font-normal" onChange={(event) => update("dueAt", event.target.value)} type="date" value={form.dueAt} /></label>
        <label className="block text-sm font-semibold text-on-surface">Minutes<input className="mt-1.5 w-full rounded-lg border border-outline-variant/40 bg-surface px-3 py-2.5 text-sm font-normal" min="1" onChange={(event) => update("estimatedMinutes", event.target.value)} type="number" value={form.estimatedMinutes} /></label>
      </div>
      <footer className="flex justify-end gap-3 border-t border-outline-variant/20 pt-4"><button className="rounded-lg px-4 py-2 text-sm font-semibold text-on-surface-variant hover:bg-surface-container" onClick={onCancel} type="button">Cancel</button><button className="rounded-lg bg-primary px-4 py-2 text-sm font-bold text-on-primary disabled:opacity-50" disabled={saving || !form.title.trim()} type="submit">{saving ? "Adding" : "Add task"}</button></footer>
    </form>
  );
}

function ProjectStatePill({ state }) {
  const colors = { active: "bg-primary/10 text-primary", completed: "bg-secondary/10 text-secondary", archived: "bg-surface-container text-on-surface-variant" };
  return <span className={`rounded-md px-2 py-1 text-[11px] font-bold ${colors[state] || colors.active}`}>{titleCase(state)}</span>;
}

export default function ProjectsView() {
  const router = useRouter();
  const [projects, setProjects] = useState([]);
  const [loading, setLoading] = useState(true);
  const [query, setQuery] = useState("");
  const [state, setState] = useState("active");
  const [createOpen, setCreateOpen] = useState(false);
  const [saving, setSaving] = useState(false);
  const [notice, setNotice] = useState(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setProjects(await projectsApi.list({ includeArchived: state === "all", state: state === "all" ? undefined : state }));
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "Unable to load projects.") });
    } finally {
      setLoading(false);
    }
  }, [state]);

  useEffect(() => { void load(); }, [load]);

  const visibleProjects = useMemo(() => projects.filter((project) => `${project.name} ${project.description || ""}`.toLowerCase().includes(query.toLowerCase())), [projects, query]);
  const createProject = async (input) => {
    setSaving(true);
    try {
      const project = await projectsApi.create(input);
      setCreateOpen(false);
      router.push(`/projects/${project.id}?view=list`);
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "Unable to create the project.") });
    } finally {
      setSaving(false);
    }
  };
  const archiveProject = async (project) => {
    try {
      const archived = project.state === "archived";
      await (archived ? projectsApi.restore(project.id, project.concurrencyToken) : projectsApi.archive(project.id, project.concurrencyToken));
      setNotice({ type: "success", message: archived ? "Project restored." : "Project archived." });
      await load();
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "Unable to update the project.") });
    }
  };

  return (
    <div className="mx-auto w-full max-w-7xl px-4 py-6 sm:px-6 sm:py-8 lg:px-8">
      <Toast notice={notice} onDismiss={() => setNotice(null)} />
      <header className="flex flex-col gap-5 border-b border-outline-variant/20 pb-6 lg:flex-row lg:items-end lg:justify-between">
        <div><p className="text-xs font-bold uppercase text-primary">Workspace</p><h1 className="mt-1 text-3xl font-bold text-on-surface">Projects</h1><p className="mt-2 max-w-xl text-sm text-on-surface-variant">Plan the work once, then use the view that helps you move it forward.</p></div>
        <button className="flex items-center justify-center gap-2 rounded-lg bg-primary px-4 py-2.5 text-sm font-bold text-on-primary" onClick={() => setCreateOpen(true)} type="button"><span className="material-symbols-outlined" style={{ fontSize: "18px" }}>add</span>New project</button>
      </header>
      <div className="mt-5 flex flex-col gap-3 sm:flex-row sm:items-center">
        <label className="relative flex-1"><span className="sr-only">Search projects</span><span className="material-symbols-outlined absolute left-3 top-1/2 -translate-y-1/2 text-on-surface-variant" style={{ fontSize: "18px" }}>search</span><input className="w-full rounded-lg border border-outline-variant/40 bg-white py-2.5 pl-10 pr-3 text-sm" onChange={(event) => setQuery(event.target.value)} placeholder="Search projects" value={query} /></label>
        <select aria-label="Filter project state" className="rounded-lg border border-outline-variant/40 bg-white px-3 py-2.5 text-sm font-semibold text-on-surface" onChange={(event) => setState(event.target.value)} value={state}><option value="active">Active</option><option value="completed">Completed</option><option value="archived">Archived</option><option value="all">All projects</option></select>
      </div>
      <section className="mt-5 overflow-hidden rounded-lg border border-outline-variant/25 bg-white">
        <div className="overflow-x-auto">
          <table className="w-full min-w-[720px] text-left"><thead className="bg-surface-container-low text-xs font-bold uppercase text-on-surface-variant"><tr><th className="px-5 py-3">Project</th><th className="px-4 py-3">State</th><th className="px-4 py-3">Progress</th><th className="px-4 py-3">Target</th><th className="w-16 px-4 py-3"><span className="sr-only">Actions</span></th></tr></thead><tbody className="divide-y divide-outline-variant/15">
            {loading && [0, 1, 2].map((index) => <tr key={index}><td className="px-5 py-5" colSpan="5"><div className="h-5 w-full animate-pulse rounded bg-surface-container" /></td></tr>)}
            {!loading && visibleProjects.map((project) => <tr className="group hover:bg-surface-container-low/50" key={project.id}><td className="px-5 py-4"><button className="flex min-w-0 items-center gap-3 text-left" onClick={() => router.push(`/projects/${project.id}?view=list`)} type="button"><span className="flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-lg text-white" style={{ background: projectColor(project.color) }}><span className="material-symbols-outlined" style={{ fontSize: "19px" }}>{project.icon || "folder"}</span></span><span className="min-w-0"><span className="block truncate text-sm font-bold text-on-surface">{project.name}</span>{project.description && <span className="mt-0.5 block truncate text-xs text-on-surface-variant">{project.description}</span>}</span></button></td><td className="px-4 py-4"><ProjectStatePill state={project.state} /></td><td className="px-4 py-4"><div className="flex min-w-32 items-center gap-2"><div className="h-1.5 flex-1 overflow-hidden rounded-full bg-surface-container-high"><div className="h-full rounded-full" style={{ background: projectColor(project.color), width: `${project.progressPercent}%` }} /></div><span className="text-xs font-bold text-on-surface">{project.progressPercent}%</span></div><span className="mt-1 block text-xs text-on-surface-variant">{project.completedTaskCount}/{project.totalTaskCount} tasks</span></td><td className="px-4 py-4 text-sm text-on-surface-variant">{shortDate(project.targetAt)}</td><td className="px-4 py-4"><IconButton icon={project.state === "archived" ? "unarchive" : "archive"} label={`${project.state === "archived" ? "Restore" : "Archive"} ${project.name}`} onClick={() => archiveProject(project)} /></td></tr>)}
            {!loading && visibleProjects.length === 0 && <tr><td className="px-5 py-14 text-center" colSpan="5"><span className="material-symbols-outlined text-on-surface-variant/35" style={{ fontSize: "40px" }}>folder_open</span><p className="mt-2 text-sm font-semibold text-on-surface-variant">No projects found</p></td></tr>}
          </tbody></table>
        </div>
      </section>
      {createOpen && <Modal onClose={() => !saving && setCreateOpen(false)} title="New project"><ProjectForm onCancel={() => setCreateOpen(false)} onSave={createProject} saving={saving} /></Modal>}
    </div>
  );
}

function SortableTaskCard({ task }) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({ id: task.id, data: { type: "project-task", taskId: task.id } });
  const state = TASK_STATES.find((item) => item.id === task.state) || TASK_STATES[0];
  return <article ref={setNodeRef} style={{ transform: CSS.Transform.toString(transform), transition, opacity: isDragging ? 0.45 : 1, borderLeftColor: state.color }} {...attributes} {...listeners} className="cursor-grab rounded-lg border border-outline-variant/25 border-l-[3px] bg-white p-3 shadow-sm active:cursor-grabbing"><div className="flex items-start justify-between gap-2"><h3 className="text-sm font-semibold leading-snug text-on-surface">{task.title}</h3><span className="material-symbols-outlined text-on-surface-variant/50" style={{ fontSize: "16px" }}>drag_indicator</span></div>{task.description && <p className="mt-2 line-clamp-2 text-xs leading-relaxed text-on-surface-variant">{task.description}</p>}<div className="mt-3 flex items-center justify-between gap-2 text-[11px] font-semibold text-on-surface-variant"><span>{titleCase(task.lifeArea)}</span><span>{shortDate(task.dueAt)}</span></div></article>;
}

function BoardColumn({ state, tasks }) {
  const { setNodeRef, isOver } = useDroppable({ id: `column-${state.id}`, data: { type: "project-column", state: state.id } });
  return <section className="flex w-72 flex-shrink-0 flex-col" ref={setNodeRef}><header className="mb-3 flex items-center justify-between px-1"><span className="flex items-center gap-2 text-xs font-bold uppercase text-on-surface-variant"><span className="h-2 w-2 rounded-full" style={{ background: state.color }} />{state.label}</span><span className="rounded-md bg-surface-container px-1.5 py-0.5 text-[11px] font-semibold text-on-surface-variant">{tasks.length}</span></header><div className={`min-h-[22rem] space-y-3 rounded-lg border p-3 ${isOver ? "border-primary/40 bg-primary/5" : "border-outline-variant/20 bg-surface-container-low/60"}`}><SortableContext items={tasks.map((task) => task.id)} strategy={verticalListSortingStrategy}>{tasks.map((task) => <SortableTaskCard key={task.id} task={task} />)}</SortableContext>{tasks.length === 0 && <p className="pt-12 text-center text-xs font-medium text-on-surface-variant/60">Drop a task here</p>}</div></section>;
}

function ProjectListView({ onUpdateTask, tasks }) {
  return <div className="overflow-hidden rounded-lg border border-outline-variant/25 bg-white"><div className="overflow-x-auto"><table className="w-full min-w-[760px] text-left"><thead className="bg-surface-container-low text-xs font-bold uppercase text-on-surface-variant"><tr><th className="px-4 py-3">Task</th><th className="px-3 py-3">Status</th><th className="px-3 py-3">Area</th><th className="px-3 py-3">Start</th><th className="px-3 py-3">Due</th></tr></thead><tbody className="divide-y divide-outline-variant/15">{tasks.map((task) => <tr className="hover:bg-surface-container-low/50" key={task.id}><td className="px-4 py-3"><p className="text-sm font-semibold text-on-surface">{task.title}</p>{task.description && <p className="mt-0.5 max-w-md truncate text-xs text-on-surface-variant">{task.description}</p>}</td><td className="px-3 py-3"><select className="rounded-md border border-outline-variant/35 bg-white px-2 py-1.5 text-xs font-semibold" onChange={(event) => onUpdateTask(task, { state: event.target.value })} value={task.state}>{TASK_STATES.map((state) => <option key={state.id} value={state.id}>{state.label}</option>)}</select></td><td className="px-3 py-3 text-sm text-on-surface-variant">{titleCase(task.lifeArea)}</td><td className="px-3 py-3 text-sm text-on-surface-variant">{shortDate(task.startAt)}</td><td className="px-3 py-3 text-sm text-on-surface-variant">{shortDate(task.dueAt)}</td></tr>)}{tasks.length === 0 && <tr><td className="px-4 py-12 text-center text-sm text-on-surface-variant" colSpan="5">Create the first task for this project.</td></tr>}</tbody></table></div></div>;
}

function ProjectCalendarView({ tasks }) {
  const [cursor, setCursor] = useState(() => new Date());
  const first = new Date(cursor.getFullYear(), cursor.getMonth(), 1);
  const last = new Date(cursor.getFullYear(), cursor.getMonth() + 1, 0);
  const days = Array.from({ length: first.getDay() + last.getDate() }, (_, index) => index - first.getDay() + 1);
  const tasksByDay = tasks.reduce((groups, task) => { const key = dayKey(task.dueAt); if (key) (groups[key] ||= []).push(task); return groups; }, {});
  return <section className="overflow-hidden rounded-lg border border-outline-variant/25 bg-white"><header className="flex items-center justify-between border-b border-outline-variant/20 px-4 py-3"><IconButton icon="chevron_left" label="Previous month" onClick={() => setCursor(new Date(cursor.getFullYear(), cursor.getMonth() - 1, 1))} /><h2 className="text-sm font-bold text-on-surface">{cursor.toLocaleDateString(undefined, { month: "long", year: "numeric" })}</h2><IconButton icon="chevron_right" label="Next month" onClick={() => setCursor(new Date(cursor.getFullYear(), cursor.getMonth() + 1, 1))} /></header><div className="grid grid-cols-7 border-b border-outline-variant/15 bg-surface-container-low text-center text-[11px] font-bold uppercase text-on-surface-variant">{["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"].map((day) => <span className="py-2" key={day}>{day}</span>)}</div><div className="grid min-w-[640px] grid-cols-7">{days.map((day, index) => { if (day < 1) return <div className="min-h-28 border-b border-r border-outline-variant/15 bg-surface-container-low/40" key={`blank-${index}`} />; const date = new Date(cursor.getFullYear(), cursor.getMonth(), day); const key = date.toISOString().slice(0, 10); return <div className="min-h-28 border-b border-r border-outline-variant/15 p-2" key={key}><span className="text-xs font-bold text-on-surface-variant">{day}</span><div className="mt-1 space-y-1">{(tasksByDay[key] || []).slice(0, 3).map((task) => <p className="truncate rounded-md bg-primary/10 px-1.5 py-1 text-[10px] font-semibold text-primary" key={task.id} title={task.title}>{task.title}</p>)}</div></div>; })}</div><div className="border-t border-outline-variant/15 px-4 py-3 text-xs text-on-surface-variant">{tasks.filter((task) => !task.dueAt).length} unscheduled task{tasks.filter((task) => !task.dueAt).length === 1 ? "" : "s"}</div></section>;
}

function ProjectTimelineView({ tasks }) {
  const dated = tasks.filter((task) => task.startAt || task.dueAt);
  const start = dated.length ? new Date(Math.min(...dated.map((task) => new Date(task.startAt || task.dueAt).getTime()))) : new Date();
  start.setDate(start.getDate() - start.getDay());
  const days = Array.from({ length: 42 }, (_, index) => { const date = new Date(start); date.setDate(start.getDate() + index); return date; });
  const position = (value) => Math.max(0, Math.min(41, Math.floor((new Date(value).getTime() - start.getTime()) / 86400000)));
  return <div className="overflow-x-auto rounded-lg border border-outline-variant/25 bg-white"><div className="min-w-[900px]"><div className="grid grid-cols-[15rem_1fr] border-b border-outline-variant/20"><div className="px-4 py-3 text-xs font-bold uppercase text-on-surface-variant">Task</div><div className="grid grid-cols-6">{Array.from({ length: 6 }, (_, week) => <div className="border-l border-outline-variant/15 px-2 py-3 text-xs font-bold text-on-surface-variant" key={week}>{days[week * 7].toLocaleDateString(undefined, { month: "short", day: "numeric" })}</div>)}</div></div>{dated.map((task) => { const left = position(task.startAt || task.dueAt); const right = position(task.dueAt || task.startAt); const width = Math.max(1, right - left + 1); return <div className="grid grid-cols-[15rem_1fr] border-b border-outline-variant/15 last:border-none" key={task.id}><div className="truncate px-4 py-3 text-sm font-semibold text-on-surface" title={task.title}>{task.title}</div><div className="relative h-11 bg-[linear-gradient(to_right,transparent_calc(16.666%-1px),rgba(172,179,183,.25)_calc(16.666%-1px),rgba(172,179,183,.25)_16.666%,transparent_16.666%)]"><div className="absolute top-3 h-5 rounded-md bg-primary px-2 text-[10px] font-bold leading-5 text-on-primary" style={{ left: `${(left / 42) * 100}%`, width: `${(width / 42) * 100}%` }} title={`${shortDate(task.startAt)} - ${shortDate(task.dueAt)}`}>{width > 4 ? task.title : ""}</div></div></div>; })}{dated.length === 0 && <p className="px-4 py-12 text-center text-sm text-on-surface-variant">Add dates to tasks to see a timeline.</p>}</div></div>;
}

export function ProjectWorkspace({ projectId }) {
  const router = useRouter();
  const searchParams = useSearchParams();
  const view = VIEWS.some((item) => item.id === searchParams.get("view")) ? searchParams.get("view") : "list";
  const [project, setProject] = useState(null);
  const [tasks, setTasks] = useState([]);
  const [loading, setLoading] = useState(true);
  const [notice, setNotice] = useState(null);
  const [taskOpen, setTaskOpen] = useState(false);
  const [projectOpen, setProjectOpen] = useState(false);
  const [saving, setSaving] = useState(false);
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 6 } }));

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [nextProject, nextTasks] = await Promise.all([projectsApi.get(projectId), tasksApi.list({ projectId })]);
      setProject(nextProject);
      setTasks(nextTasks);
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "Unable to load this project.") });
    } finally {
      setLoading(false);
    }
  }, [projectId]);

  useEffect(() => { void load(); }, [load]);
  const changeView = (nextView) => router.replace(`/projects/${projectId}?view=${nextView}`, { scroll: false });
  const updateTask = async (task, changes) => {
    try {
      const updated = await tasksApi.update(task.id, { ...task, ...changes, concurrencyToken: task.concurrencyToken });
      setTasks((current) => current.map((item) => item.id === task.id ? updated : item));
      void load();
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "The task could not be updated. Refresh and try again.") });
    }
  };
  const createTask = async (input) => {
    setSaving(true);
    try {
      const created = await tasksApi.create({ ...input, projectId, quadrant: "unsorted" });
      setTaskOpen(false);
      await load();
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "Unable to add the task.") });
    } finally { setSaving(false); }
  };
  const saveProject = async (input) => {
    setSaving(true);
    try {
      const updated = await projectsApi.update(project.id, { ...input, state: project.state, sortOrder: project.sortOrder, concurrencyToken: project.concurrencyToken });
      setProject(updated);
      setProjectOpen(false);
    } catch (error) { setNotice({ type: "error", message: messageFrom(error, "Unable to save the project.") }); } finally { setSaving(false); }
  };
  const handleDragEnd = async ({ active, over }) => {
    if (!over || !project) return;
    const activeTask = tasks.find((task) => task.id === active.id);
    if (!activeTask) return;
    const overTask = tasks.find((task) => task.id === over.id);
    const nextState = overTask?.state || over.data.current?.state;
    if (!nextState) return;
    const beforeTaskId = overTask && overTask.id !== activeTask.id ? overTask.id : null;
    try {
      setTasks((current) => current.map((task) => task.id === activeTask.id ? { ...task, state: nextState } : task));
      await projectsApi.reorderTask(project.id, { taskId: activeTask.id, state: nextState, beforeTaskId, concurrencyToken: activeTask.concurrencyToken });
      await load();
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "The board changed elsewhere. It has been refreshed.") });
      await load();
    }
  };
  const archive = async () => {
    try {
      const archived = project.state === "archived";
      await (archived ? projectsApi.restore(project.id, project.concurrencyToken) : projectsApi.archive(project.id, project.concurrencyToken));
      if (archived) {
        await load();
      } else {
        router.replace("/projects");
        router.refresh();
      }
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "Unable to update the project.") });
    }
  };

  const groupedTasks = TASK_STATES.reduce((groups, state) => ({ ...groups, [state.id]: tasks.filter((task) => task.state === state.id).sort((left, right) => left.sortOrder - right.sortOrder) }), {});
  if (loading && !project) return <div className="px-6 py-10 text-sm text-on-surface-variant">Loading project...</div>;
  if (!project) return <div className="px-6 py-10 text-sm text-error">This project is unavailable.</div>;

  return <div className="mx-auto w-full max-w-[96rem] px-4 py-6 sm:px-6 sm:py-8 lg:px-8"><Toast notice={notice} onDismiss={() => setNotice(null)} /><header className="border-b border-outline-variant/20 pb-5"><div className="flex items-start justify-between gap-4"><div className="flex min-w-0 items-start gap-3"><button aria-label="Back to projects" className="mt-1 flex h-8 w-8 flex-shrink-0 items-center justify-center rounded-lg text-on-surface-variant hover:bg-surface-container" onClick={() => router.push("/projects")} type="button"><span className="material-symbols-outlined" style={{ fontSize: "19px" }}>arrow_back</span></button><span className="flex h-11 w-11 flex-shrink-0 items-center justify-center rounded-lg text-white" style={{ background: projectColor(project.color) }}><span className="material-symbols-outlined" style={{ fontSize: "23px" }}>{project.icon || "folder"}</span></span><div className="min-w-0"><div className="flex flex-wrap items-center gap-2"><h1 className="truncate text-2xl font-bold text-on-surface sm:text-3xl">{project.name}</h1><ProjectStatePill state={project.state} /></div>{project.description && <p className="mt-1 max-w-3xl text-sm text-on-surface-variant">{project.description}</p>}<p className="mt-2 text-xs font-semibold text-on-surface-variant">{project.completedTaskCount}/{project.totalTaskCount} tasks complete · Target {shortDate(project.targetAt)}</p></div></div><div className="flex gap-1"><IconButton icon="edit" label="Edit project" onClick={() => setProjectOpen(true)} /><IconButton icon={project.state === "archived" ? "unarchive" : "archive"} label={project.state === "archived" ? "Restore project" : "Archive project"} onClick={archive} /></div></div><div className="mt-5 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between"><div className="flex w-full overflow-x-auto rounded-lg border border-outline-variant/30 bg-white p-1 sm:w-auto">{VIEWS.map((item) => <button aria-pressed={view === item.id} className={`flex min-w-24 items-center justify-center gap-1.5 rounded-md px-3 py-2 text-xs font-bold transition-colors ${view === item.id ? "bg-primary text-on-primary" : "text-on-surface-variant hover:bg-surface-container"}`} key={item.id} onClick={() => changeView(item.id)} type="button"><span className="material-symbols-outlined" style={{ fontSize: "16px" }}>{item.icon}</span>{item.label}</button>)}</div><button className="flex items-center justify-center gap-2 rounded-lg bg-primary px-4 py-2 text-sm font-bold text-on-primary" disabled={project.state === "archived"} onClick={() => setTaskOpen(true)} type="button"><span className="material-symbols-outlined" style={{ fontSize: "17px" }}>add</span>Add task</button></div></header><main className="mt-5">{view === "list" && <ProjectListView onUpdateTask={updateTask} tasks={tasks} />}{view === "board" && <DndContext onDragEnd={handleDragEnd} sensors={sensors}><div className="flex gap-4 overflow-x-auto pb-4">{TASK_STATES.map((state) => <BoardColumn key={state.id} state={state} tasks={groupedTasks[state.id]} />)}</div></DndContext>}{view === "calendar" && <ProjectCalendarView tasks={tasks} />}{view === "timeline" && <ProjectTimelineView tasks={tasks} />}</main>{taskOpen && <Modal onClose={() => !saving && setTaskOpen(false)} title="Add task"><TaskForm onCancel={() => setTaskOpen(false)} onSave={createTask} saving={saving} /></Modal>}{projectOpen && <Modal onClose={() => !saving && setProjectOpen(false)} title="Edit project"><ProjectForm onCancel={() => setProjectOpen(false)} onSave={saveProject} project={project} saving={saving} /></Modal>}</div>;
}
