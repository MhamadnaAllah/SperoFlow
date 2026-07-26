"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { DndContext, PointerSensor, useDraggable, useDroppable, useSensor, useSensors } from "@dnd-kit/core";

import BrainDumpModal from "./BrainDumpModal";
import { aiApi, aiProposalsApi, ApiError, tasksApi } from "@/lib/api/client";

const QUADRANTS = [
  { id: "unsorted", title: "Unsorted", description: "Capture first, decide next.", color: "#64748b" },
  { id: "q1", title: "Do now", description: "Urgent and important", color: "#b42318" },
  { id: "q2", title: "Schedule", description: "Important, not urgent", color: "#0053dc" },
  { id: "q3", title: "Delegate", description: "Urgent, not important", color: "#865400" },
  { id: "q4", title: "Eliminate", description: "Neither urgent nor important", color: "#596063" },
];

function messageFrom(error, fallback) {
  return error instanceof ApiError ? error.message : fallback;
}

function taskUpdate(task, changes) {
  return {
    title: task.title,
    description: task.description || null,
    lifeArea: task.lifeArea,
    quadrant: task.quadrant,
    state: task.state,
    startAt: task.startAt || null,
    dueAt: task.dueAt || null,
    estimatedMinutes: task.estimatedMinutes || null,
    reminderAt: task.reminderAt || null,
    projectId: task.projectId || null,
    roleId: task.roleId || null,
    goalId: task.goalId || null,
    sortOrder: task.sortOrder || 0,
    concurrencyToken: task.concurrencyToken,
    ...changes,
  };
}

function quadrantLabel(id) {
  return QUADRANTS.find((item) => item.id === id)?.title || "Review";
}

function ProposalReview({ busy, onApprove, onCancel, proposal }) {
  const suggested = proposal.payload?.quadrant || "unsorted";
  return (
    <section className="mt-3 border-l-2 border-primary bg-primary/5 px-3 py-2.5">
      <div className="flex items-start gap-2">
        <span className="material-symbols-outlined mt-0.5 text-primary" style={{ fontSize: "16px" }}>auto_awesome</span>
        <div className="min-w-0 flex-1">
          <p className="text-xs font-bold text-on-surface">AI suggests: {quadrantLabel(suggested)}</p>
          <p className="mt-1 text-xs leading-relaxed text-on-surface-variant">{proposal.description}</p>
          <div className="mt-2 flex flex-wrap gap-2">
            <button className="rounded-md border border-outline-variant/40 px-2.5 py-1.5 text-xs font-bold text-on-surface hover:bg-white disabled:opacity-50" disabled={busy} onClick={() => onCancel(proposal)} type="button">Cancel</button>
            <button className="rounded-md bg-primary px-2.5 py-1.5 text-xs font-bold text-on-primary disabled:opacity-50" disabled={busy} onClick={() => onApprove(proposal)} type="button">Approve</button>
          </div>
        </div>
      </div>
    </section>
  );
}

function DraggableTask({ busy, onApprove, onCancel, onClassify, proposal, task }) {
  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({
    id: task.id,
    data: { taskId: task.id },
  });
  const style = transform ? { transform: "translate3d(" + transform.x + "px, " + transform.y + "px, 0)" } : undefined;
  return (
    <article ref={setNodeRef} style={{ ...style, opacity: isDragging ? 0.45 : 1 }} {...attributes} className="rounded-lg border border-outline-variant/25 bg-white p-3 shadow-sm">
      <div className="flex items-start justify-between gap-2">
        <p {...listeners} className="min-w-0 flex-1 cursor-grab text-sm font-semibold leading-snug text-on-surface active:cursor-grabbing">{task.title}</p>
        <button aria-label={"Classify " + task.title} className="flex h-7 w-7 flex-shrink-0 items-center justify-center rounded-md text-primary hover:bg-primary/10 disabled:opacity-40" disabled={busy || Boolean(proposal)} onClick={() => onClassify(task)} onPointerDown={(event) => event.stopPropagation()} title={proposal ? "A priority suggestion is waiting for your review." : "Ask AI to suggest a quadrant"} type="button">
          <span className="material-symbols-outlined" style={{ fontSize: "17px" }}>{busy ? "progress_activity" : "auto_awesome"}</span>
        </button>
      </div>
      {task.description && <p className="mt-2 line-clamp-2 text-xs leading-relaxed text-on-surface-variant">{task.description}</p>}
      <div className="mt-3 flex items-center justify-between gap-2 text-[11px] font-bold text-on-surface-variant">
        <span>{task.lifeArea}</span>
        <span>{task.dueAt ? new Date(task.dueAt).toLocaleDateString(undefined, { month: "short", day: "numeric" }) : "No due date"}</span>
      </div>
      {proposal && <ProposalReview busy={busy} onApprove={onApprove} onCancel={onCancel} proposal={proposal} />}
    </article>
  );
}

function Quadrant({ busyTaskId, onApprove, onCancel, onClassify, proposalsByTask, quadrant, tasks }) {
  const { isOver, setNodeRef } = useDroppable({ id: quadrant.id, data: { quadrantId: quadrant.id } });
  return (
    <section ref={setNodeRef} className={"min-h-[16rem] rounded-lg border p-4 transition-colors " + (isOver ? "border-primary/45 bg-primary/5" : "border-outline-variant/25 bg-surface-container-low/60")}>
      <header className="mb-3 border-b border-outline-variant/15 pb-3">
        <div className="flex items-center justify-between gap-2">
          <h2 className="flex items-center gap-2 text-sm font-bold text-on-surface"><span className="h-2.5 w-2.5 rounded-full" style={{ background: quadrant.color }} />{quadrant.title}</h2>
          <span className="rounded-md bg-white px-2 py-0.5 text-xs font-bold text-on-surface-variant">{tasks.length}</span>
        </div>
        <p className="mt-1 text-xs text-on-surface-variant">{quadrant.description}</p>
      </header>
      <div className="space-y-3">
        {tasks.map((task) => <DraggableTask busy={busyTaskId === task.id} key={task.id} onApprove={onApprove} onCancel={onCancel} onClassify={onClassify} proposal={proposalsByTask.get(task.id)} task={task} />)}
        {tasks.length === 0 && <p className="py-10 text-center text-xs text-on-surface-variant/65">Drop a task here</p>}
      </div>
    </section>
  );
}

export default function MatrixView() {
  const [tasks, setTasks] = useState([]);
  const [proposals, setProposals] = useState([]);
  const [loading, setLoading] = useState(true);
  const [brainDumpOpen, setBrainDumpOpen] = useState(false);
  const [creating, setCreating] = useState(false);
  const [busyTaskId, setBusyTaskId] = useState(null);
  const [notice, setNotice] = useState(null);
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 6 } }));

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const values = await Promise.all([
        tasksApi.list(),
        aiProposalsApi.list({ state: "pending" }),
      ]);
      setTasks(values[0]);
      setProposals(values[1].filter((proposal) => proposal.kind === "applyTaskClassification" && typeof proposal.payload?.taskId === "string"));
      setNotice(null);
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "Unable to load the Matrix.") });
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const update = async (task, changes) => {
    const saved = await tasksApi.update(task.id, taskUpdate(task, changes));
    setTasks((current) => current.map((item) => item.id === task.id ? saved : item));
    return saved;
  };

  const classify = async (task) => {
    setBusyTaskId(task.id);
    try {
      const proposal = await aiApi.proposeTaskClassification(task.id);
      setProposals((current) => [...current.filter((item) => item.payload?.taskId !== task.id), proposal]);
      setNotice({ type: "success", message: "A priority suggestion is ready for your decision." });
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "The task could not be classified.") });
    } finally {
      setBusyTaskId(null);
    }
  };

  const approveProposal = async (proposal) => {
    setBusyTaskId(proposal.payload?.taskId || null);
    try {
      await aiProposalsApi.approve(proposal.id, proposal.concurrencyToken);
      setProposals((current) => current.filter((item) => item.id !== proposal.id));
      await load();
      setNotice({ type: "success", message: "The approved priority is now on the Matrix." });
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "Unable to approve the priority suggestion.") });
      await load();
    } finally {
      setBusyTaskId(null);
    }
  };

  const cancelProposal = async (proposal) => {
    setBusyTaskId(proposal.payload?.taskId || null);
    try {
      await aiProposalsApi.cancel(proposal.id, proposal.concurrencyToken);
      setProposals((current) => current.filter((item) => item.id !== proposal.id));
      setNotice({ type: "success", message: "The priority suggestion was cancelled." });
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "Unable to cancel the priority suggestion.") });
      await load();
    } finally {
      setBusyTaskId(null);
    }
  };

  const move = async ({ active, over }) => {
    if (!over) return;
    const task = tasks.find((item) => item.id === active.id);
    const quadrant = over.data.current?.quadrantId || String(over.id);
    if (!task || !QUADRANTS.some((item) => item.id === quadrant) || task.quadrant === quadrant) return;
    setTasks((current) => current.map((item) => item.id === task.id ? { ...item, quadrant } : item));
    setProposals((current) => current.filter((proposal) => proposal.payload?.taskId !== task.id));
    try {
      await update(task, { quadrant });
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "The move could not be saved. The Matrix was refreshed.") });
      await load();
    }
  };

  const brainDump = async (titles) => {
    setCreating(true);
    try {
      const created = [];
      for (const title of titles) {
        created.push(await tasksApi.create({
          title,
          description: null,
          lifeArea: "personal",
          quadrant: "unsorted",
          estimatedMinutes: null,
          dueAt: null,
          startAt: null,
          projectId: null,
          roleId: null,
          goalId: null,
        }));
      }
      setTasks((current) => [...current, ...created]);
      setBrainDumpOpen(false);
      setNotice({ type: "success", message: String(created.length) + " task" + (created.length === 1 ? "" : "s") + " added to Unsorted." });
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "Some tasks could not be added.") });
      await load();
    } finally {
      setCreating(false);
    }
  };

  const groups = useMemo(
    () => Object.fromEntries(QUADRANTS.map((quadrant) => [quadrant.id, tasks.filter((task) => (task.quadrant || "unsorted") === quadrant.id)])),
    [tasks],
  );
  const proposalsByTask = useMemo(
    () => new Map(proposals.map((proposal) => [proposal.payload.taskId, proposal])),
    [proposals],
  );
  const classified = tasks.filter((task) => task.quadrant && task.quadrant !== "unsorted").length;

  return (
    <div className="mx-auto w-full max-w-[96rem] px-4 py-6 sm:px-6 sm:py-8 lg:px-8">
      <header className="flex flex-col gap-4 border-b border-outline-variant/20 pb-5 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <p className="text-xs font-bold uppercase text-primary">Prioritization</p>
          <h1 className="mt-1 text-3xl font-bold text-on-surface">Eisenhower Matrix</h1>
          <p className="mt-2 text-sm text-on-surface-variant">Drag tasks when you decide. AI priority suggestions stay pending until you approve them.</p>
        </div>
        <button className="flex items-center justify-center gap-2 rounded-lg bg-primary px-4 py-2.5 text-sm font-bold text-on-primary" onClick={() => setBrainDumpOpen(true)} type="button">
          <span className="material-symbols-outlined" style={{ fontSize: "18px" }}>add</span>
          Brain dump
        </button>
      </header>

      {notice && <div className={"mt-4 flex items-center justify-between gap-3 rounded-lg border px-4 py-3 text-sm font-medium " + (notice.type === "error" ? "border-error/20 bg-error/10 text-error" : "border-secondary/20 bg-secondary/10 text-secondary")}>
        <span>{notice.message}</span>
        <button aria-label="Dismiss notice" className="flex h-7 w-7 items-center justify-center" onClick={() => setNotice(null)} type="button"><span className="material-symbols-outlined" style={{ fontSize: "18px" }}>close</span></button>
      </div>}

      <div className="mt-5 grid gap-3 sm:grid-cols-3">
        <div className="rounded-lg border border-outline-variant/20 bg-white p-4"><p className="text-xs font-bold uppercase text-on-surface-variant">Tasks</p><p className="mt-1 text-2xl font-bold text-on-surface">{tasks.length}</p></div>
        <div className="rounded-lg border border-outline-variant/20 bg-white p-4"><p className="text-xs font-bold uppercase text-on-surface-variant">Classified</p><p className="mt-1 text-2xl font-bold text-primary">{classified}</p></div>
        <div className="rounded-lg border border-outline-variant/20 bg-white p-4"><p className="text-xs font-bold uppercase text-on-surface-variant">Awaiting review</p><p className="mt-1 text-2xl font-bold text-on-surface">{proposals.length}</p></div>
      </div>

      <main className="mt-5">
        {loading ? <div className="grid gap-4 lg:grid-cols-2">{QUADRANTS.map((quadrant) => <div className="h-72 animate-pulse rounded-lg bg-surface-container" key={quadrant.id} />)}</div> : <DndContext onDragEnd={move} sensors={sensors}><div className="grid gap-4 lg:grid-cols-2">{QUADRANTS.map((quadrant) => <Quadrant busyTaskId={busyTaskId} key={quadrant.id} onApprove={approveProposal} onCancel={cancelProposal} onClassify={classify} proposalsByTask={proposalsByTask} quadrant={quadrant} tasks={groups[quadrant.id]} />)}</div></DndContext>}
      </main>

      <BrainDumpModal loading={creating} onClose={() => setBrainDumpOpen(false)} onSubmit={brainDump} open={brainDumpOpen} />
    </div>
  );
}