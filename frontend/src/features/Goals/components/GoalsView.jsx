"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";

import { ApiError, goalsApi, rolesApi } from "@/lib/api/client";

const LIFE_AREAS = ["work", "family", "physical", "spiritual", "social", "learning", "personal"];

function messageFrom(error, fallback) {
  return error instanceof ApiError ? error.message : fallback;
}

function titleCase(value) {
  return value ? value.charAt(0).toUpperCase() + value.slice(1) : "";
}

function toDateInput(value) {
  return value ? value.slice(0, 10) : "";
}

function toIsoDate(value) {
  return value ? new Date(value + "T00:00:00.000Z").toISOString() : null;
}

function updatePayload(goal, state) {
  return {
    title: goal.title,
    description: goal.description,
    lifeArea: goal.lifeArea,
    targetAt: goal.targetAt,
    state,
    sortOrder: goal.sortOrder,
    roleId: goal.roleId,
    concurrencyToken: goal.concurrencyToken,
  };
}

function GoalForm({ onCancel, onSave, roles, saving }) {
  const [form, setForm] = useState({
    title: "",
    description: "",
    lifeArea: "personal",
    targetAt: "",
    roleId: "",
  });
  const update = (field, value) => setForm((current) => ({ ...current, [field]: value }));

  return (
    <form className="border-y border-outline-variant/20 py-5" onSubmit={(event) => { event.preventDefault(); void onSave(form); }}>
      <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_12rem_12rem]">
        <label className="block">
          <span className="text-xs font-bold uppercase text-on-surface-variant">Goal</span>
          <input autoFocus className="mt-1.5 w-full border border-outline-variant/40 bg-white px-3 py-2.5 text-sm text-on-surface outline-none focus:border-primary" maxLength={240} onChange={(event) => update("title", event.target.value)} placeholder="Describe a meaningful outcome" value={form.title} />
        </label>
        <label className="block">
          <span className="text-xs font-bold uppercase text-on-surface-variant">Life area</span>
          <select className="mt-1.5 w-full border border-outline-variant/40 bg-white px-3 py-2.5 text-sm text-on-surface outline-none focus:border-primary" onChange={(event) => update("lifeArea", event.target.value)} value={form.lifeArea}>
            {LIFE_AREAS.map((area) => <option key={area} value={area}>{titleCase(area)}</option>)}
          </select>
        </label>
        <label className="block">
          <span className="text-xs font-bold uppercase text-on-surface-variant">Target date</span>
          <input className="mt-1.5 w-full border border-outline-variant/40 bg-white px-3 py-2.5 text-sm text-on-surface outline-none focus:border-primary" onChange={(event) => update("targetAt", event.target.value)} type="date" value={form.targetAt} />
        </label>
      </div>
      <div className="mt-4 grid gap-4 lg:grid-cols-[minmax(0,1fr)_18rem]">
        <label className="block">
          <span className="text-xs font-bold uppercase text-on-surface-variant">Context</span>
          <textarea className="mt-1.5 min-h-24 w-full resize-y border border-outline-variant/40 bg-white px-3 py-2.5 text-sm text-on-surface outline-none focus:border-primary" maxLength={8000} onChange={(event) => update("description", event.target.value)} placeholder="Why this outcome matters, or what success looks like" value={form.description} />
        </label>
        <label className="block">
          <span className="text-xs font-bold uppercase text-on-surface-variant">Life role</span>
          <select className="mt-1.5 w-full border border-outline-variant/40 bg-white px-3 py-2.5 text-sm text-on-surface outline-none focus:border-primary" onChange={(event) => update("roleId", event.target.value)} value={form.roleId}>
            <option value="">No specific role</option>
            {roles.filter((role) => !role.isArchived).map((role) => <option key={role.id} value={role.id}>{role.name}</option>)}
          </select>
        </label>
      </div>
      <div className="mt-4 flex justify-end gap-2">
        <button className="rounded-lg px-3 py-2 text-sm font-bold text-on-surface hover:bg-surface-container disabled:opacity-50" disabled={saving} onClick={onCancel} type="button">Cancel</button>
        <button className="rounded-lg bg-primary px-4 py-2 text-sm font-bold text-on-primary disabled:opacity-50" disabled={saving || !form.title.trim()} type="submit">{saving ? "Saving" : "Create goal"}</button>
      </div>
    </form>
  );
}

function GoalRow({ onArchive, onComplete, onOpen, roleName, saving, goal }) {
  const isArchived = goal.state === "archived";
  const isCompleted = goal.state === "completed";
  return (
    <article className="grid gap-3 border-b border-outline-variant/20 py-4 last:border-b-0 sm:grid-cols-[minmax(0,1fr)_auto] sm:items-center">
      <div className="min-w-0">
        <div className="flex flex-wrap items-center gap-2">
          <span className={"rounded-md px-2 py-1 text-[11px] font-bold uppercase " + (isCompleted ? "bg-secondary/10 text-secondary" : isArchived ? "bg-surface-container text-on-surface-variant" : "bg-primary/10 text-primary")}>{titleCase(goal.state)}</span>
          <span className="text-xs font-semibold text-on-surface-variant">{titleCase(goal.lifeArea)}{roleName ? " - " + roleName : ""}</span>
        </div>
        <button className="mt-2 text-left text-base font-bold text-on-surface hover:text-primary" onClick={() => onOpen(goal.id)} type="button">{goal.title}</button>
        {goal.description && <p className="mt-1 max-w-3xl text-sm leading-relaxed text-on-surface-variant">{goal.description}</p>}
        <div className="mt-3 flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-on-surface-variant">
          <span>{goal.progressPercent}% complete</span>
          <span>{goal.completedMilestoneCount}/{goal.totalMilestoneCount} milestones</span>
          <span>{goal.completedTaskCount}/{goal.totalTaskCount} linked tasks</span>
          {goal.targetAt && <span>Target {new Date(goal.targetAt).toLocaleDateString(undefined, { month: "short", day: "numeric", year: "numeric" })}</span>}
        </div>
      </div>
      <div className="flex items-center gap-1 sm:self-start">
        <button aria-label={"Open " + goal.title} className="flex h-9 w-9 items-center justify-center rounded-lg text-on-surface-variant hover:bg-surface-container hover:text-primary" disabled={saving} onClick={() => onOpen(goal.id)} title="Open goal" type="button"><span className="material-symbols-outlined" style={{ fontSize: "19px" }}>arrow_forward</span></button>
        {!isArchived && !isCompleted && <button aria-label={"Complete " + goal.title} className="flex h-9 w-9 items-center justify-center rounded-lg text-on-surface-variant hover:bg-surface-container hover:text-secondary" disabled={saving} onClick={() => onComplete(goal)} title="Mark complete" type="button"><span className="material-symbols-outlined" style={{ fontSize: "19px" }}>check_circle</span></button>}
        {!isArchived && <button aria-label={"Archive " + goal.title} className="flex h-9 w-9 items-center justify-center rounded-lg text-on-surface-variant hover:bg-surface-container hover:text-error" disabled={saving} onClick={() => onArchive(goal)} title="Archive goal" type="button"><span className="material-symbols-outlined" style={{ fontSize: "19px" }}>archive</span></button>}
      </div>
    </article>
  );
}

export default function GoalsView() {
  const router = useRouter();
  const [goals, setGoals] = useState([]);
  const [roles, setRoles] = useState([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [creating, setCreating] = useState(false);
  const [notice, setNotice] = useState(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const values = await Promise.all([
        goalsApi.list({ includeArchived: true }),
        rolesApi.list({ includeArchived: true }),
      ]);
      setGoals(values[0]);
      setRoles(values[1]);
      setNotice(null);
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "Unable to load goals.") });
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const roleNames = useMemo(() => new Map(roles.map((role) => [role.id, role.name])), [roles]);
  const orderedGoals = useMemo(
    () => [...goals].sort((left, right) => left.state.localeCompare(right.state) || left.sortOrder - right.sortOrder || left.title.localeCompare(right.title)),
    [goals],
  );

  const createGoal = async (form) => {
    setSaving(true);
    try {
      const goal = await goalsApi.create({
        title: form.title,
        description: form.description || null,
        lifeArea: form.lifeArea,
        targetAt: toIsoDate(form.targetAt),
        roleId: form.roleId || null,
      });
      setGoals((current) => [...current, goal]);
      setCreating(false);
      router.push("/goals/" + goal.id);
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "Unable to create the goal.") });
    } finally {
      setSaving(false);
    }
  };

  const changeState = async (goal, state) => {
    setSaving(true);
    try {
      const updated = await goalsApi.update(goal.id, updatePayload(goal, state));
      setGoals((current) => current.map((item) => item.id === updated.id ? updated : item));
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "Unable to update the goal.") });
      await load();
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="mx-auto w-full max-w-[76rem] px-4 py-6 sm:px-6 sm:py-8 lg:px-8">
      <header className="flex flex-col gap-4 border-b border-outline-variant/20 pb-5 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <p className="text-xs font-bold uppercase text-primary">Direction</p>
          <h1 className="mt-1 text-3xl font-bold text-on-surface">Goals</h1>
          <p className="mt-2 max-w-2xl text-sm text-on-surface-variant">Keep your most meaningful outcomes visible, then review graph-grounded roadmap suggestions before they change the plan.</p>
        </div>
        <button className="flex items-center justify-center gap-2 rounded-lg bg-primary px-4 py-2.5 text-sm font-bold text-on-primary" onClick={() => setCreating((value) => !value)} type="button">
          <span className="material-symbols-outlined" style={{ fontSize: "18px" }}>add</span>
          New goal
        </button>
      </header>

      {notice && <div className={"mt-4 flex items-center justify-between gap-3 rounded-lg border px-4 py-3 text-sm font-medium " + (notice.type === "error" ? "border-error/20 bg-error/10 text-error" : "border-secondary/20 bg-secondary/10 text-secondary")}>
        <span>{notice.message}</span>
        <button aria-label="Dismiss notice" className="flex h-7 w-7 items-center justify-center" onClick={() => setNotice(null)} type="button"><span className="material-symbols-outlined" style={{ fontSize: "18px" }}>close</span></button>
      </div>}

      {creating && <GoalForm onCancel={() => setCreating(false)} onSave={createGoal} roles={roles} saving={saving} />}

      <section className="mt-6">
        <div className="flex items-center justify-between">
          <div>
            <h2 className="text-lg font-bold text-on-surface">Your goals</h2>
            <p className="mt-1 text-sm text-on-surface-variant">Open a goal to review milestones, linked work, and GraphRAG roadmap proposals.</p>
          </div>
          <span className="rounded-md bg-surface-container px-2 py-1 text-xs font-bold text-on-surface-variant">{goals.length}</span>
        </div>
        <div className="mt-3 border-t border-outline-variant/20">
          {loading ? <div className="h-48 animate-pulse bg-surface-container-low" /> : orderedGoals.length > 0 ? orderedGoals.map((goal) => <GoalRow goal={goal} key={goal.id} onArchive={(value) => changeState(value, "archived")} onComplete={(value) => changeState(value, "completed")} onOpen={(id) => router.push("/goals/" + id)} roleName={goal.roleId ? roleNames.get(goal.roleId) : null} saving={saving} />) : <p className="py-12 text-sm text-on-surface-variant">Create your first goal to begin shaping a focused path.</p>}
        </div>
      </section>
    </div>
  );
}