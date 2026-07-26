"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { DndContext, PointerSensor, useDroppable, useSensor, useSensors } from "@dnd-kit/core";
import { SortableContext, verticalListSortingStrategy, useSortable } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";

import { ApiError, projectsApi, rolesApi, tasksApi } from "@/lib/api/client";

const COLUMNS = [
  { id: "todo", title: "To do", color: "#64748b" },
  { id: "inProgress", title: "In progress", color: "#0053dc" },
  { id: "completed", title: "Completed", color: "#006d4a" },
  { id: "cancelled", title: "Cancelled", color: "#ac3434" },
];
const LIFE_AREAS = ["work", "family", "physical", "spiritual", "social", "learning", "personal"];

function titleCase(value) {
  return String(value || "").replace(/([A-Z])/g, " $1").replace(/^./, (character) => character.toUpperCase());
}

function dateAt(value, hour = 9) {
  return value ? `${value}T${String(hour).padStart(2, "0")}:00:00.000Z` : null;
}

function shortDate(value) {
  return value ? new Date(value).toLocaleDateString(undefined, { month: "short", day: "numeric" }) : "No due date";
}

function TaskModal({ onClose, onSave, projects, roles, saving }) {
  const [form, setForm] = useState({ title: "", description: "", lifeArea: "work", roleId: "", projectId: "", startAt: "", dueAt: "", estimatedMinutes: "" });
  const update = (key, value) => setForm((current) => ({ ...current, [key]: value }));
  return <div className="fixed inset-0 z-[60] flex items-center justify-center bg-slate-950/35 p-4" onMouseDown={onClose}><form aria-modal="true" className="w-full max-w-lg rounded-lg border border-outline-variant/30 bg-white shadow-2xl" onMouseDown={(event) => event.stopPropagation()} onSubmit={(event) => { event.preventDefault(); if (!form.title.trim()) return; onSave({ title: form.title.trim(), description: form.description.trim() || null, lifeArea: form.lifeArea, quadrant: "unsorted", state: "todo", projectId: form.projectId || null, roleId: form.roleId || null, startAt: dateAt(form.startAt), dueAt: dateAt(form.dueAt, 17), estimatedMinutes: form.estimatedMinutes ? Number(form.estimatedMinutes) : null }); }}>
    <header className="flex items-center justify-between border-b border-outline-variant/20 px-5 py-4"><h2 className="text-lg font-bold text-on-surface">New task</h2><button aria-label="Close" className="flex h-8 w-8 items-center justify-center rounded-lg text-on-surface-variant hover:bg-surface-container" onClick={onClose} type="button"><span className="material-symbols-outlined">close</span></button></header>
    <div className="space-y-4 p-5"><label className="block text-sm font-semibold text-on-surface">Task<input autoFocus className="mt-1.5 w-full rounded-lg border border-outline-variant/40 bg-surface px-3 py-2.5 text-sm font-normal" onChange={(event) => update("title", event.target.value)} value={form.title} /></label><label className="block text-sm font-semibold text-on-surface">Details<textarea className="mt-1.5 min-h-20 w-full resize-y rounded-lg border border-outline-variant/40 bg-surface px-3 py-2.5 text-sm font-normal" onChange={(event) => update("description", event.target.value)} value={form.description} /></label><div className="grid gap-4 sm:grid-cols-3"><label className="block text-sm font-semibold text-on-surface">Life area<select className="mt-1.5 w-full rounded-lg border border-outline-variant/40 bg-surface px-3 py-2.5 text-sm font-normal" onChange={(event) => update("lifeArea", event.target.value)} value={form.lifeArea}>{LIFE_AREAS.map((area) => <option key={area} value={area}>{titleCase(area)}</option>)}</select></label><label className="block text-sm font-semibold text-on-surface">Role<select className="mt-1.5 w-full rounded-lg border border-outline-variant/40 bg-surface px-3 py-2.5 text-sm font-normal" onChange={(event) => { const role = roles.find((item) => item.id === event.target.value); setForm((current) => ({ ...current, roleId: event.target.value, lifeArea: role?.defaultLifeArea || current.lifeArea })); }} value={form.roleId}><option value="">No specific role</option>{roles.map((role) => <option key={role.id} value={role.id}>{role.name}</option>)}</select></label><label className="block text-sm font-semibold text-on-surface">Project<select className="mt-1.5 w-full rounded-lg border border-outline-variant/40 bg-surface px-3 py-2.5 text-sm font-normal" onChange={(event) => update("projectId", event.target.value)} value={form.projectId}><option value="">No project</option>{projects.map((project) => <option key={project.id} value={project.id}>{project.name}</option>)}</select></label></div><div className="grid gap-4 sm:grid-cols-3"><label className="block text-sm font-semibold text-on-surface">Start<input className="mt-1.5 w-full rounded-lg border border-outline-variant/40 bg-surface px-3 py-2.5 text-sm font-normal" onChange={(event) => update("startAt", event.target.value)} type="date" value={form.startAt} /></label><label className="block text-sm font-semibold text-on-surface">Due<input className="mt-1.5 w-full rounded-lg border border-outline-variant/40 bg-surface px-3 py-2.5 text-sm font-normal" onChange={(event) => update("dueAt", event.target.value)} type="date" value={form.dueAt} /></label><label className="block text-sm font-semibold text-on-surface">Minutes<input className="mt-1.5 w-full rounded-lg border border-outline-variant/40 bg-surface px-3 py-2.5 text-sm font-normal" min="1" onChange={(event) => update("estimatedMinutes", event.target.value)} type="number" value={form.estimatedMinutes} /></label></div></div><footer className="flex justify-end gap-3 border-t border-outline-variant/20 px-5 py-4"><button className="rounded-lg px-4 py-2 text-sm font-semibold text-on-surface-variant hover:bg-surface-container" onClick={onClose} type="button">Cancel</button><button className="rounded-lg bg-primary px-4 py-2 text-sm font-bold text-on-primary disabled:opacity-50" disabled={saving || !form.title.trim()} type="submit">{saving ? "Adding" : "Add task"}</button></footer>
  </form></div>;
}

function SortableTask({ onDelete, projectName, roleName, task }) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({ id: task.id, data: { type: "global-task", taskId: task.id } });
  return <article ref={setNodeRef} style={{ transform: CSS.Transform.toString(transform), transition, opacity: isDragging ? 0.45 : 1 }} {...attributes} {...listeners} className="cursor-grab rounded-lg border border-outline-variant/25 bg-white p-3 shadow-sm active:cursor-grabbing"><div className="flex items-start justify-between gap-2"><h3 className={`text-sm font-semibold leading-snug text-on-surface ${task.state === "completed" ? "line-through opacity-60" : ""}`}>{task.title}</h3><button aria-label={`Delete ${task.title}`} className="text-on-surface-variant hover:text-error" onClick={(event) => { event.stopPropagation(); onDelete(task); }} onPointerDown={(event) => event.stopPropagation()} type="button"><span className="material-symbols-outlined" style={{ fontSize: "16px" }}>close</span></button></div>{task.description && <p className="mt-2 line-clamp-2 text-xs leading-relaxed text-on-surface-variant">{task.description}</p>}<div className="mt-3 flex flex-wrap items-center justify-between gap-2 text-[11px] font-semibold text-on-surface-variant"><span>{roleName || projectName || titleCase(task.lifeArea)}</span><span>{shortDate(task.dueAt)}</span></div></article>;
}

function Column({ column, onDelete, projectsById, rolesById, tasks }) {
  const { setNodeRef, isOver } = useDroppable({ id: `global-${column.id}`, data: { type: "global-column", state: column.id } });
  return <section className="flex w-72 flex-shrink-0 flex-col" ref={setNodeRef}><header className="mb-3 flex items-center justify-between px-1"><span className="flex items-center gap-2 text-xs font-bold uppercase text-on-surface-variant"><span className="h-2 w-2 rounded-full" style={{ background: column.color }} />{column.title}</span><span className="rounded-md bg-surface-container px-1.5 py-0.5 text-[11px] font-semibold text-on-surface-variant">{tasks.length}</span></header><div className={`min-h-[26rem] space-y-3 rounded-lg border p-3 ${isOver ? "border-primary/40 bg-primary/5" : "border-outline-variant/20 bg-surface-container-low/60"}`}><SortableContext items={tasks.map((task) => task.id)} strategy={verticalListSortingStrategy}>{tasks.map((task) => <SortableTask key={task.id} onDelete={onDelete} projectName={projectsById[task.projectId]?.name} roleName={rolesById[task.roleId]?.name} task={task} />)}</SortableContext>{tasks.length === 0 && <p className="pt-14 text-center text-xs text-on-surface-variant/60">Drop a task here</p>}</div></section>;
}

export default function TasksView() {
  const [tasks, setTasks] = useState([]);
  const [projects, setProjects] = useState([]);
  const [roles, setRoles] = useState([]);
  const [loading, setLoading] = useState(true);
  const [createOpen, setCreateOpen] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState(null);
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 6 } }));
  const load = useCallback(async () => { setLoading(true); try { const [nextTasks, nextProjects, nextRoles] = await Promise.all([tasksApi.list(), projectsApi.list(), rolesApi.bootstrap()]); setTasks(nextTasks); setProjects(nextProjects); setRoles(nextRoles); setError(null); } catch (requestError) { setError(requestError instanceof ApiError ? requestError.message : "Unable to load tasks."); } finally { setLoading(false); } }, []);
  useEffect(() => { void load(); }, [load]);
  const projectsById = useMemo(() => Object.fromEntries(projects.map((project) => [project.id, project])), [projects]);
  const rolesById = useMemo(() => Object.fromEntries(roles.map((role) => [role.id, role])), [roles]);
  const update = async (task, changes) => { try { const saved = await tasksApi.update(task.id, { ...task, ...changes, concurrencyToken: task.concurrencyToken }); setTasks((current) => current.map((item) => item.id === task.id ? saved : item)); } catch (requestError) { setError(requestError instanceof ApiError ? requestError.message : "Task changed elsewhere. Refresh and try again."); await load(); } };
  const onDragEnd = async ({ active, over }) => { if (!over) return; const task = tasks.find((item) => item.id === active.id); if (!task) return; const overTask = tasks.find((item) => item.id === over.id); const state = overTask?.state || over.data.current?.state; if (!state || state === task.state) return; setTasks((current) => current.map((item) => item.id === task.id ? { ...item, state } : item)); await update(task, { state }); };
  const create = async (input) => { setSaving(true); try { const task = await tasksApi.create(input); setTasks((current) => [...current, task]); setCreateOpen(false); } catch (requestError) { setError(requestError instanceof ApiError ? requestError.message : "Unable to create task."); } finally { setSaving(false); } };
  const remove = async (task) => { try { await tasksApi.remove(task.id, task.concurrencyToken); setTasks((current) => current.filter((item) => item.id !== task.id)); } catch (requestError) { setError(requestError instanceof ApiError ? requestError.message : "Unable to delete task."); } };
  const grouped = COLUMNS.reduce((groups, column) => ({ ...groups, [column.id]: tasks.filter((task) => task.state === column.id).sort((left, right) => left.sortOrder - right.sortOrder) }), {});
  return <div className="mx-auto flex h-full w-full max-w-[96rem] flex-col px-4 py-6 sm:px-6 sm:py-8 lg:px-8"><header className="flex flex-col gap-4 border-b border-outline-variant/20 pb-5 sm:flex-row sm:items-end sm:justify-between"><div><p className="text-xs font-bold uppercase text-primary">Execution</p><h1 className="mt-1 text-3xl font-bold text-on-surface">Tasks</h1><p className="mt-2 text-sm text-on-surface-variant">Move work forward across your personal and project queues.</p></div><button className="flex items-center justify-center gap-2 rounded-lg bg-primary px-4 py-2.5 text-sm font-bold text-on-primary" onClick={() => setCreateOpen(true)} type="button"><span className="material-symbols-outlined" style={{ fontSize: "18px" }}>add</span>New task</button></header>{error && <div className="mt-4 flex items-center justify-between rounded-lg border border-error/20 bg-error/10 px-4 py-3 text-sm font-medium text-error"><span>{error}</span><button className="text-xs font-bold underline" onClick={() => setError(null)} type="button">Dismiss</button></div>}<main className="mt-5 min-h-0 flex-1">{loading ? <div className="flex gap-4 overflow-hidden">{COLUMNS.map((column) => <div className="h-[26rem] w-72 flex-shrink-0 animate-pulse rounded-lg bg-surface-container" key={column.id} />)}</div> : <DndContext onDragEnd={onDragEnd} sensors={sensors}><div className="flex gap-4 overflow-x-auto pb-4">{COLUMNS.map((column) => <Column column={column} key={column.id} onDelete={remove} projectsById={projectsById} rolesById={rolesById} tasks={grouped[column.id]} />)}</div></DndContext>}</main>{createOpen && <TaskModal onClose={() => !saving && setCreateOpen(false)} onSave={create} projects={projects} roles={roles} saving={saving} />}</div>;
}