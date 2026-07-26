"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { useParams, useRouter } from "next/navigation";

import { aiProposalsApi, ApiError, goalsApi, tasksApi } from "@/lib/api/client";

function messageFrom(error, fallback) {
  return error instanceof ApiError ? error.message : fallback;
}

function titleCase(value) {
  return value ? value.charAt(0).toUpperCase() + value.slice(1) : "";
}

function updateGoalPayload(goal, state) {
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

function milestonePayload(milestone, state) {
  return {
    title: milestone.title,
    description: milestone.description,
    estimatedHours: milestone.estimatedHours,
    state,
    sortOrder: milestone.sortOrder,
    concurrencyToken: milestone.concurrencyToken,
  };
}

function MilestoneRow({ busy, milestone, onStateChange }) {
  const complete = milestone.state === "completed";
  return (
    <article className="flex items-start gap-3 border-b border-outline-variant/20 py-4 last:border-b-0">
      <button aria-label={(complete ? "Reopen " : "Complete ") + milestone.title} className={"mt-0.5 flex h-6 w-6 flex-shrink-0 items-center justify-center rounded border " + (complete ? "border-secondary bg-secondary text-on-primary" : "border-outline-variant/60 text-transparent hover:border-primary")} disabled={busy || milestone.state === "archived"} onClick={() => onStateChange(milestone, complete ? "pending" : "completed")} type="button">
        <span className="material-symbols-outlined" style={{ fontSize: "16px" }}>check</span>
      </button>
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-start justify-between gap-2">
          <h3 className={"text-sm font-bold " + (complete ? "text-on-surface-variant line-through" : "text-on-surface")}>{milestone.title}</h3>
          {milestone.estimatedHours !== null && <span className="rounded-md bg-surface-container px-2 py-1 text-xs font-bold text-on-surface-variant">{milestone.estimatedHours}h</span>}
        </div>
        {milestone.description && <p className="mt-1.5 text-sm leading-relaxed text-on-surface-variant">{milestone.description}</p>}
      </div>
    </article>
  );
}

function RoadmapReview({ busy, onApprove, onCancel, proposal }) {
  return (
    <section className="border-y border-primary/20 bg-primary/5 py-5">
      <div className="flex flex-col gap-4 px-4 sm:flex-row sm:items-start sm:justify-between sm:px-5">
        <div>
          <p className="text-xs font-bold uppercase text-primary">GraphRAG proposal</p>
          <h2 className="mt-1 text-lg font-bold text-on-surface">Review this roadmap before it is added</h2>
          <p className="mt-2 max-w-3xl text-sm leading-relaxed text-on-surface-variant">{proposal.roadmap.summary}</p>
        </div>
        <div className="flex flex-shrink-0 gap-2">
          <button className="rounded-lg border border-outline-variant/40 px-3 py-2 text-sm font-bold text-on-surface hover:bg-white disabled:opacity-50" disabled={busy} onClick={() => onCancel(proposal.proposal)} type="button">Cancel</button>
          <button className="rounded-lg bg-primary px-3 py-2 text-sm font-bold text-on-primary disabled:opacity-50" disabled={busy} onClick={() => onApprove(proposal.proposal)} type="button">Approve</button>
        </div>
      </div>
      <ol className="mt-5 divide-y divide-primary/15 border-t border-primary/15 px-4 sm:px-5">
        {proposal.roadmap.steps.map((step, index) => <li className="flex gap-3 py-4" key={step.sortOrder + "-" + step.title}>
          <span className="flex h-7 w-7 flex-shrink-0 items-center justify-center rounded-md bg-primary text-xs font-bold text-on-primary">{index + 1}</span>
          <div className="min-w-0 flex-1">
            <div className="flex flex-wrap items-start justify-between gap-2"><h3 className="text-sm font-bold text-on-surface">{step.title}</h3>{step.estimatedHours !== null && <span className="text-xs font-semibold text-on-surface-variant">{step.estimatedHours}h</span>}</div>
            {step.description && <p className="mt-1 text-sm leading-relaxed text-on-surface-variant">{step.description}</p>}
          </div>
        </li>)}
      </ol>
    </section>
  );
}

function MilestoneForm({ busy, onCancel, onSave }) {
  const [form, setForm] = useState({ title: "", description: "", estimatedHours: "" });
  const update = (field, value) => setForm((current) => ({ ...current, [field]: value }));
  return (
    <form className="border-y border-outline-variant/20 py-4" onSubmit={(event) => { event.preventDefault(); void onSave(form); }}>
      <div className="grid gap-3 sm:grid-cols-[minmax(0,1fr)_9rem]">
        <label><span className="sr-only">Milestone title</span><input autoFocus className="w-full border border-outline-variant/40 bg-white px-3 py-2 text-sm text-on-surface outline-none focus:border-primary" maxLength={300} onChange={(event) => update("title", event.target.value)} placeholder="Milestone title" value={form.title} /></label>
        <label><span className="sr-only">Estimated hours</span><input className="w-full border border-outline-variant/40 bg-white px-3 py-2 text-sm text-on-surface outline-none focus:border-primary" min="0" onChange={(event) => update("estimatedHours", event.target.value)} placeholder="Hours" step="0.5" type="number" value={form.estimatedHours} /></label>
      </div>
      <label className="mt-3 block"><span className="sr-only">Milestone details</span><textarea className="min-h-20 w-full resize-y border border-outline-variant/40 bg-white px-3 py-2 text-sm text-on-surface outline-none focus:border-primary" maxLength={4000} onChange={(event) => update("description", event.target.value)} placeholder="What does this milestone make possible?" value={form.description} /></label>
      <div className="mt-3 flex justify-end gap-2"><button className="rounded-lg px-3 py-2 text-sm font-bold text-on-surface hover:bg-surface-container disabled:opacity-50" disabled={busy} onClick={onCancel} type="button">Cancel</button><button className="rounded-lg bg-primary px-3 py-2 text-sm font-bold text-on-primary disabled:opacity-50" disabled={busy || !form.title.trim()} type="submit">{busy ? "Saving" : "Add milestone"}</button></div>
    </form>
  );
}

export default function GoalDetailView() {
  const params = useParams();
  const router = useRouter();
  const goalId = typeof params?.goalId === "string" ? params.goalId : "";
  const [goal, setGoal] = useState(null);
  const [milestones, setMilestones] = useState([]);
  const [linkedTasks, setLinkedTasks] = useState([]);
  const [roadmapProposal, setRoadmapProposal] = useState(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [addingMilestone, setAddingMilestone] = useState(false);
  const [notice, setNotice] = useState(null);

  const load = useCallback(async () => {
    if (!goalId) return;
    setLoading(true);
    try {
      const values = await Promise.all([
        goalsApi.get(goalId),
        goalsApi.listMilestones(goalId),
        tasksApi.list({ goalId }),
        goalsApi.listPendingRoadmaps(),
      ]);
      setGoal(values[0]);
      setMilestones(values[1]);
      setLinkedTasks(values[2]);
      setRoadmapProposal(values[3].find((proposal) => proposal.goalId === goalId) || null);
      setNotice(null);
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "Unable to load this goal.") });
    } finally {
      setLoading(false);
    }
  }, [goalId]);

  useEffect(() => {
    void load();
  }, [load]);

  const remainingTasks = useMemo(() => linkedTasks.filter((task) => task.state !== "completed" && task.state !== "cancelled"), [linkedTasks]);

  const createRoadmap = async () => {
    if (!goal) return;
    setBusy(true);
    try {
      const proposal = await goalsApi.proposeRoadmap(goal.id);
      setRoadmapProposal(proposal);
      setNotice({ type: "success", message: "A graph-grounded roadmap is ready for your review." });
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "Unable to generate a roadmap right now.") });
    } finally {
      setBusy(false);
    }
  };

  const approveRoadmap = async (proposal) => {
    setBusy(true);
    try {
      await aiProposalsApi.approve(proposal.id, proposal.concurrencyToken);
      setRoadmapProposal(null);
      await load();
      setNotice({ type: "success", message: "The approved roadmap is now part of this goal." });
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "Unable to approve the roadmap.") });
      await load();
    } finally {
      setBusy(false);
    }
  };

  const cancelRoadmap = async (proposal) => {
    setBusy(true);
    try {
      await aiProposalsApi.cancel(proposal.id, proposal.concurrencyToken);
      setRoadmapProposal(null);
      setNotice({ type: "success", message: "The roadmap proposal was cancelled." });
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "Unable to cancel the roadmap proposal.") });
      await load();
    } finally {
      setBusy(false);
    }
  };

  const addMilestone = async (form) => {
    if (!goal) return;
    setBusy(true);
    try {
      const milestone = await goalsApi.createMilestone(goal.id, {
        title: form.title,
        description: form.description || null,
        estimatedHours: form.estimatedHours === "" ? null : Number(form.estimatedHours),
      });
      setMilestones((current) => [...current, milestone]);
      setAddingMilestone(false);
      await load();
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "Unable to add the milestone.") });
    } finally {
      setBusy(false);
    }
  };

  const updateMilestoneState = async (milestone, state) => {
    if (!goal) return;
    setBusy(true);
    try {
      const updated = await goalsApi.updateMilestone(goal.id, milestone.id, milestonePayload(milestone, state));
      setMilestones((current) => current.map((item) => item.id === updated.id ? updated : item));
      await load();
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "Unable to update the milestone.") });
      await load();
    } finally {
      setBusy(false);
    }
  };

  const completeGoal = async () => {
    if (!goal) return;
    setBusy(true);
    try {
      const updated = await goalsApi.update(goal.id, updateGoalPayload(goal, "completed"));
      setGoal(updated);
      setNotice({ type: "success", message: "Goal marked complete." });
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "Unable to update the goal.") });
    } finally {
      setBusy(false);
    }
  };

  if (loading) {
    return <div className="mx-auto w-full max-w-[76rem] px-4 py-8 sm:px-6 lg:px-8"><div className="h-10 w-64 animate-pulse bg-surface-container" /><div className="mt-8 h-72 animate-pulse bg-surface-container-low" /></div>;
  }

  if (!goal) {
    return <div className="mx-auto w-full max-w-[76rem] px-4 py-8 sm:px-6 lg:px-8"><button className="flex items-center gap-2 text-sm font-bold text-primary" onClick={() => router.push("/goals")} type="button"><span className="material-symbols-outlined">arrow_back</span>All goals</button><p className="mt-8 text-sm text-on-surface-variant">This goal is unavailable.</p></div>;
  }

  return (
    <div className="mx-auto w-full max-w-[76rem] px-4 py-6 sm:px-6 sm:py-8 lg:px-8">
      <header className="border-b border-outline-variant/20 pb-5">
        <button className="flex items-center gap-2 text-sm font-bold text-primary hover:text-primary-dim" onClick={() => router.push("/goals")} type="button"><span className="material-symbols-outlined" style={{ fontSize: "18px" }}>arrow_back</span>All goals</button>
        <div className="mt-5 flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
          <div>
            <div className="flex flex-wrap items-center gap-2"><span className="rounded-md bg-primary/10 px-2 py-1 text-[11px] font-bold uppercase text-primary">{titleCase(goal.lifeArea)}</span><span className="text-xs font-semibold text-on-surface-variant">{titleCase(goal.state)}</span></div>
            <h1 className="mt-3 text-3xl font-bold text-on-surface">{goal.title}</h1>
            {goal.description && <p className="mt-3 max-w-3xl text-sm leading-relaxed text-on-surface-variant">{goal.description}</p>}
            <div className="mt-4 flex flex-wrap gap-x-4 gap-y-1 text-xs text-on-surface-variant"><span>{goal.progressPercent}% complete</span><span>{goal.completedMilestoneCount}/{goal.totalMilestoneCount} milestones</span><span>{goal.completedTaskCount}/{goal.totalTaskCount} tasks</span>{goal.targetAt && <span>Target {new Date(goal.targetAt).toLocaleDateString(undefined, { month: "short", day: "numeric", year: "numeric" })}</span>}</div>
          </div>
          <div className="flex flex-wrap gap-2">
            {goal.state === "active" && <button className="flex items-center gap-2 rounded-lg border border-outline-variant/40 px-3 py-2 text-sm font-bold text-on-surface hover:bg-surface-container disabled:opacity-50" disabled={busy} onClick={completeGoal} type="button"><span className="material-symbols-outlined" style={{ fontSize: "18px" }}>check_circle</span>Complete</button>}
            <button className="flex items-center gap-2 rounded-lg bg-primary px-3 py-2 text-sm font-bold text-on-primary disabled:opacity-50" disabled={busy || goal.state !== "active" || Boolean(roadmapProposal)} onClick={createRoadmap} type="button"><span className="material-symbols-outlined" style={{ fontSize: "18px" }}>account_tree</span>Generate roadmap</button>
          </div>
        </div>
      </header>

      {notice && <div className={"mt-4 flex items-center justify-between gap-3 rounded-lg border px-4 py-3 text-sm font-medium " + (notice.type === "error" ? "border-error/20 bg-error/10 text-error" : "border-secondary/20 bg-secondary/10 text-secondary")}>
        <span>{notice.message}</span>
        <button aria-label="Dismiss notice" className="flex h-7 w-7 items-center justify-center" onClick={() => setNotice(null)} type="button"><span className="material-symbols-outlined" style={{ fontSize: "18px" }}>close</span></button>
      </div>}

      {roadmapProposal && <div className="mt-6"><RoadmapReview busy={busy} onApprove={approveRoadmap} onCancel={cancelRoadmap} proposal={roadmapProposal} /></div>}

      <main className="mt-8 grid gap-10 lg:grid-cols-[minmax(0,1fr)_20rem]">
        <section>
          <div className="flex items-end justify-between gap-3">
            <div><h2 className="text-lg font-bold text-on-surface">Milestones</h2><p className="mt-1 text-sm text-on-surface-variant">Complete the path one meaningful step at a time.</p></div>
            <button className="flex h-9 w-9 items-center justify-center rounded-lg text-primary hover:bg-primary/10 disabled:opacity-50" aria-label="Add milestone" disabled={busy || goal.state !== "active" || Boolean(roadmapProposal)} onClick={() => setAddingMilestone((value) => !value)} title="Add milestone" type="button"><span className="material-symbols-outlined">add</span></button>
          </div>
          {addingMilestone && <div className="mt-4"><MilestoneForm busy={busy} onCancel={() => setAddingMilestone(false)} onSave={addMilestone} /></div>}
          <div className="mt-4 border-t border-outline-variant/20">
            {milestones.length > 0 ? milestones.map((milestone) => <MilestoneRow busy={busy} key={milestone.id} milestone={milestone} onStateChange={updateMilestoneState} />) : <p className="py-10 text-sm text-on-surface-variant">Generate a roadmap or add the first milestone yourself.</p>}
          </div>
        </section>

        <aside className="border-t border-outline-variant/20 pt-5 lg:border-l lg:border-t-0 lg:pl-6 lg:pt-0">
          <h2 className="text-sm font-bold text-on-surface">Linked tasks</h2>
          <p className="mt-1 text-xs leading-relaxed text-on-surface-variant">Tasks linked to this goal remain visible in the Matrix and Calendar.</p>
          <div className="mt-4 space-y-3">
            {remainingTasks.length > 0 ? remainingTasks.slice(0, 8).map((task) => <article className="border-b border-outline-variant/20 pb-3" key={task.id}><p className="text-sm font-semibold text-on-surface">{task.title}</p><p className="mt-1 text-xs text-on-surface-variant">{titleCase(task.quadrant)} {task.dueAt ? " - due " + new Date(task.dueAt).toLocaleDateString(undefined, { month: "short", day: "numeric" }) : ""}</p></article>) : <p className="text-sm text-on-surface-variant">No active tasks are linked yet.</p>}
          </div>
        </aside>
      </main>
    </div>
  );
}