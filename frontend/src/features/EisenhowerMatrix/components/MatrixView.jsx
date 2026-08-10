"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import {
  DndContext,
  PointerSensor,
  useDraggable,
  useDroppable,
  useSensor,
  useSensors,
} from "@dnd-kit/core";

import BrainDumpModal from "./BrainDumpModal";
import { aiApi, aiProposalsApi, ApiError, tasksApi } from "@/lib/api/client";

const QUADRANTS = [
  {
    id: "q1",
    label: "DO IT NOW",
    sub: "Quadrant I",
    tag: "Urgent & Important",
    icon: "bolt",
    iconBg: "bg-amber-100",
    iconText: "text-amber-700",
    sectionBg: "rgba(251,191,36,0.06)",
    border: "#f59e0b",
    tagBg: "rgba(245,158,11,0.1)",
    tagText: "#b45309",
  },
  {
    id: "q2",
    label: "SCHEDULE IT",
    sub: "Quadrant II",
    tag: "Non-Urgent & Important",
    icon: "calendar_today",
    iconBg: "bg-green-100",
    iconText: "text-green-700",
    sectionBg: "rgba(16,185,129,0.05)",
    border: "#10b981",
    tagBg: "rgba(16,185,129,0.1)",
    tagText: "#047857",
  },
  {
    id: "q3",
    label: "DELEGATE IT",
    sub: "Quadrant III",
    tag: "Urgent & Non-Important",
    icon: "groups",
    iconBg: "bg-blue-100",
    iconText: "text-blue-700",
    sectionBg: "rgba(59,130,246,0.05)",
    border: "#3b82f6",
    tagBg: "rgba(59,130,246,0.1)",
    tagText: "#1d4ed8",
  },
  {
    id: "q4",
    label: "ELIMINATE IT",
    sub: "Quadrant IV",
    tag: "Non-Urgent & Non-Important",
    icon: "delete",
    iconBg: "bg-slate-200",
    iconText: "text-slate-500",
    sectionBg: "rgba(148,163,184,0.06)",
    border: "#94a3b8",
    tagBg: "rgba(148,163,184,0.1)",
    tagText: "#64748b",
  },
];

const UNSORTED_QUADRANT = {
  id: "unsorted",
  label: "UNSORTED INBOX",
  sub: "Capture First",
  tag: "Needs Priority Decision",
  icon: "inbox",
  iconBg: "bg-purple-100",
  iconText: "text-purple-700",
  sectionBg: "rgba(124,58,237,0.04)",
  border: "#7c3aed",
  tagBg: "rgba(124,58,237,0.1)",
  tagText: "#6d28d9",
};

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

function ProposalReview({ busy, onApprove, onCancel, proposal }) {
  const suggestedId = proposal.payload?.quadrant || "q1";
  const matched = QUADRANTS.find((q) => q.id === suggestedId) || QUADRANTS[0];

  return (
    <div className="mt-3 rounded-xl border border-purple-200 bg-purple-50/70 p-3">
      <div className="flex items-start gap-2">
        <span
          className="material-symbols-outlined text-purple-600"
          style={{ fontSize: "16px", fontVariationSettings: "'FILL' 1" }}
        >
          auto_awesome
        </span>
        <div className="min-w-0 flex-1">
          <p className="text-xs font-bold text-purple-900">
            AI Suggestion: Move to {matched.label}
          </p>
          <p className="mt-1 text-xs leading-relaxed text-purple-800">
            {proposal.description}
          </p>
          <div className="mt-2.5 flex items-center gap-2">
            <button
              onClick={() => onApprove(proposal)}
              disabled={busy}
              type="button"
              className="rounded-lg bg-purple-600 px-3 py-1 text-xs font-bold text-white shadow-sm transition-all hover:bg-purple-700 disabled:opacity-50"
            >
              Approve
            </button>
            <button
              onClick={() => onCancel(proposal)}
              disabled={busy}
              type="button"
              className="rounded-lg border border-purple-300 bg-white px-3 py-1 text-xs font-bold text-purple-700 transition-all hover:bg-purple-100 disabled:opacity-50"
            >
              Dismiss
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

function TaskCard({ busy, onApprove, onCancel, onClassify, proposal, task, quadrantId }) {
  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({
    id: task.id,
    data: { taskId: task.id, quadrantId },
  });

  const style = transform
    ? { transform: `translate3d(${transform.x}px, ${transform.y}px, 0)` }
    : undefined;

  return (
    <div
      ref={setNodeRef}
      style={{ ...style, opacity: isDragging ? 0.45 : 1 }}
      {...attributes}
      className="group relative cursor-grab rounded-2xl bg-white p-4 shadow-sm transition-all hover:shadow-md active:cursor-grabbing"
    >
      <div className="flex gap-3">
        <div {...listeners} className="pt-0.5 text-slate-300 transition-colors group-hover:text-slate-400">
          <span className="material-symbols-outlined" style={{ fontSize: "20px" }}>
            drag_indicator
          </span>
        </div>
        <div className="min-w-0 flex-1">
          <div className="flex items-start justify-between gap-2">
            <h3
              className={`text-sm font-semibold leading-tight text-on-surface ${
                quadrantId === "q4" ? "line-through text-slate-400" : ""
              }`}
            >
              {task.title}
            </h3>
            <div className="flex items-center gap-1.5">
              {task.lifeArea && (
                <span className="rounded-md bg-primary/10 px-2 py-0.5 text-[9px] font-bold uppercase tracking-tight text-primary">
                  {task.lifeArea}
                </span>
              )}
              <button
                type="button"
                onPointerDown={(e) => e.stopPropagation()}
                onClick={(e) => {
                  e.stopPropagation();
                  onClassify(task);
                }}
                disabled={busy || Boolean(proposal)}
                title={proposal ? "AI suggestion waiting" : "Analyze with AI"}
                className="flex h-6 w-6 items-center justify-center rounded-full transition-opacity hover:bg-purple-100 disabled:cursor-not-allowed"
              >
                {busy ? (
                  <span className="h-3 w-3 animate-spin rounded-full border border-purple-600 border-t-transparent" />
                ) : (
                  <span
                    className="material-symbols-outlined text-purple-600"
                    style={{ fontSize: "14px", fontVariationSettings: "'FILL' 1" }}
                  >
                    auto_awesome
                  </span>
                )}
              </button>
            </div>
          </div>

          {task.description && (
            <p className="mt-1 line-clamp-2 text-xs leading-relaxed text-on-surface-variant">
              {task.description}
            </p>
          )}

          <div className="mt-3 flex items-center justify-between gap-2 text-[10px] font-medium text-slate-400">
            <span>
              {task.dueAt
                ? `Due ${new Date(task.dueAt).toLocaleDateString(undefined, { month: "short", day: "numeric" })}`
                : "No due date"}
            </span>
            {task.estimatedMinutes && <span>⏱ {task.estimatedMinutes}m</span>}
          </div>

          {proposal && (
            <ProposalReview
              busy={busy}
              onApprove={onApprove}
              onCancel={onCancel}
              proposal={proposal}
            />
          )}
        </div>
      </div>
    </div>
  );
}

function QuadrantColumn({
  busyTaskId,
  onApprove,
  onCancel,
  onClassify,
  proposalsByTask,
  quadrant,
  tasks,
}) {
  const { isOver, setNodeRef } = useDroppable({
    id: quadrant.id,
    data: { quadrantId: quadrant.id },
  });

  return (
    <section
      ref={setNodeRef}
      className="flex min-h-[260px] flex-col gap-4 rounded-[2rem] p-5 transition-all"
      style={{
        backgroundColor: isOver
          ? quadrant.sectionBg.replace(/[\d.]+\)$/, (s) => (parseFloat(s) * 3.5).toFixed(2) + ")")
          : quadrant.sectionBg,
        border: isOver ? `2px dashed ${quadrant.border}` : "2px solid transparent",
      }}
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-3">
          <div
            className={`flex h-10 w-10 items-center justify-center rounded-2xl ${quadrant.iconBg} ${quadrant.iconText}`}
          >
            <span
              className="material-symbols-outlined"
              style={{ fontSize: "20px", fontVariationSettings: "'FILL' 1" }}
            >
              {quadrant.icon}
            </span>
          </div>
          <div>
            <span
              className="block text-[9px] font-extrabold uppercase leading-none tracking-widest"
              style={{ color: quadrant.border }}
            >
              {quadrant.sub}
            </span>
            <h2 className="text-base font-bold leading-tight text-on-surface">
              {quadrant.label}
            </h2>
          </div>
        </div>

        <div className="flex items-center gap-2">
          <span
            className="rounded-full px-2.5 py-1 text-[9px] font-bold uppercase"
            style={{ background: quadrant.tagBg, color: quadrant.tagText }}
          >
            {quadrant.tag}
          </span>
          <span className="rounded-full bg-white px-2.5 py-1 text-[10px] font-bold text-slate-500 shadow-sm">
            {tasks.length}
          </span>
        </div>
      </div>

      <div
        className="sidebar-scroll flex flex-1 flex-col gap-3 overflow-y-auto pr-1"
        style={{ maxHeight: "calc(50vh - 40px)" }}
      >
        {tasks.length === 0 && (
          <div
            className="flex flex-1 items-center justify-center rounded-2xl border-2 border-dashed py-8 text-center"
            style={{ borderColor: `${quadrant.border}40` }}
          >
            <p className="text-xs font-semibold" style={{ color: `${quadrant.border}90` }}>
              Drop tasks here
            </p>
          </div>
        )}
        {tasks.map((task) => (
          <TaskCard
            key={task.id}
            task={task}
            quadrantId={quadrant.id}
            busy={busyTaskId === task.id}
            onApprove={onApprove}
            onCancel={onCancel}
            onClassify={onClassify}
            proposal={proposalsByTask.get(task.id)}
          />
        ))}
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

  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 8 } }));

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const values = await Promise.all([
        tasksApi.list(),
        aiProposalsApi.list({ state: "pending" }),
      ]);
      setTasks(values[0]);
      setProposals(
        values[1].filter(
          (proposal) =>
            proposal.kind === "applyTaskClassification" &&
            typeof proposal.payload?.taskId === "string"
        )
      );
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
    setTasks((current) => current.map((item) => (item.id === task.id ? saved : item)));
    return saved;
  };

  const classify = async (task) => {
    setBusyTaskId(task.id);
    try {
      const proposal = await aiApi.proposeTaskClassification(task.id);
      setProposals((current) => [
        ...current.filter((item) => item.payload?.taskId !== task.id),
        proposal,
      ]);
      setNotice({ type: "success", message: "AI classification is ready for your review." });
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "Task classification failed.") });
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
      setNotice({ type: "success", message: "Approved priority updated on Matrix." });
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "Unable to approve proposal.") });
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
      setNotice({ type: "success", message: "Proposal cancelled." });
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "Unable to cancel proposal.") });
      await load();
    } finally {
      setBusyTaskId(null);
    }
  };

  const handleDragEnd = async ({ active, over }) => {
    if (!over) return;
    const task = tasks.find((item) => item.id === active.id);
    const targetQuadrant = over.data.current?.quadrantId || String(over.id);
    if (!task || task.quadrant === targetQuadrant) return;

    setTasks((current) =>
      current.map((item) => (item.id === task.id ? { ...item, quadrant: targetQuadrant } : item))
    );
    setProposals((current) => current.filter((p) => p.payload?.taskId !== task.id));

    try {
      await update(task, { quadrant: targetQuadrant });
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "Could not save move.") });
      await load();
    }
  };

  const brainDump = async (titles) => {
    setCreating(true);
    try {
      const created = [];
      for (const title of titles) {
        created.push(
          await tasksApi.create({
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
          })
        );
      }
      setTasks((current) => [...current, ...created]);
      setBrainDumpOpen(false);
      setNotice({
        type: "success",
        message: `Added ${created.length} task${created.length === 1 ? "" : "s"} to Unsorted Inbox ✨`,
      });
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "Some tasks could not be added.") });
      await load();
    } finally {
      setCreating(false);
    }
  };

  const groups = useMemo(() => {
    const result = { unsorted: [], q1: [], q2: [], q3: [], q4: [] };
    tasks.forEach((task) => {
      const q = task.quadrant && result[task.quadrant] ? task.quadrant : "unsorted";
      result[q].push(task);
    });
    return result;
  }, [tasks]);

  const proposalsByTask = useMemo(
    () => new Map(proposals.map((proposal) => [proposal.payload.taskId, proposal])),
    [proposals]
  );

  const classifiedCount = tasks.filter((t) => t.quadrant && t.quadrant !== "unsorted").length;

  return (
    <div className="mx-auto w-full max-w-7xl px-4 py-6 sm:px-6 sm:py-8 lg:px-8">
      {/* Header */}
      <header className="flex flex-col gap-4 border-b border-outline-variant/20 pb-5 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <span className="text-[10px] font-bold uppercase tracking-widest text-primary">
            Prioritization Engine
          </span>
          <h1 className="mt-1 text-3xl font-bold tracking-tight text-on-surface">
            Eisenhower Matrix
          </h1>
          <p className="mt-2 text-sm leading-relaxed text-on-surface-variant">
            Organize tasks by urgency and importance. Drag to reclassify or use AI assistant.
          </p>
        </div>

        <button
          className="flex items-center justify-center gap-2 rounded-2xl bg-primary px-5 py-3 text-sm font-bold text-on-primary shadow-md transition-all hover:bg-primary/90"
          onClick={() => setBrainDumpOpen(true)}
          type="button"
        >
          <span className="material-symbols-outlined" style={{ fontSize: "18px" }}>
            add
          </span>
          Brain dump
        </button>
      </header>

      {/* Notice Banner */}
      {notice && (
        <div
          className={`mt-4 flex items-center justify-between gap-3 rounded-xl border px-4 py-3 text-sm font-medium ${
            notice.type === "error"
              ? "border-red-200 bg-red-50 text-red-700"
              : "border-emerald-200 bg-emerald-50 text-emerald-800"
          }`}
        >
          <span>{notice.message}</span>
          <button onClick={() => setNotice(null)} type="button">
            <span className="material-symbols-outlined" style={{ fontSize: "18px" }}>
              close
            </span>
          </button>
        </div>
      )}

      {/* Stats bar */}
      <div className="mt-6 grid grid-cols-1 gap-4 sm:grid-cols-3">
        <div className="rounded-2xl border border-slate-200/80 bg-white p-4 shadow-sm">
          <p className="text-[10px] font-bold uppercase tracking-wider text-slate-400">Total Tasks</p>
          <p className="mt-1 text-2xl font-extrabold text-slate-800">{tasks.length}</p>
        </div>
        <div className="rounded-2xl border border-slate-200/80 bg-white p-4 shadow-sm">
          <p className="text-[10px] font-bold uppercase tracking-wider text-slate-400">Classified</p>
          <p className="mt-1 text-2xl font-extrabold text-primary">{classifiedCount}</p>
        </div>
        <div className="rounded-2xl border border-slate-200/80 bg-white p-4 shadow-sm">
          <p className="text-[10px] font-bold uppercase tracking-wider text-slate-400">Awaiting AI Review</p>
          <p className="mt-1 text-2xl font-extrabold text-purple-600">{proposals.length}</p>
        </div>
      </div>

      <DndContext sensors={sensors} onDragEnd={handleDragEnd}>
        {/* Unsorted Inbox (if any unsorted tasks exist) */}
        {!loading && groups.unsorted.length > 0 && (
          <div className="mt-6">
            <QuadrantColumn
              quadrant={UNSORTED_QUADRANT}
              tasks={groups.unsorted}
              busyTaskId={busyTaskId}
              onApprove={approveProposal}
              onCancel={cancelProposal}
              onClassify={classify}
              proposalsByTask={proposalsByTask}
            />
          </div>
        )}

        {/* 4 Quadrants Grid */}
        <main className="mt-6">
          {loading ? (
            <div className="grid gap-6 lg:grid-cols-2">
              {QUADRANTS.map((q) => (
                <div
                  key={q.id}
                  className="h-64 animate-pulse rounded-[2rem] bg-slate-100"
                />
              ))}
            </div>
          ) : (
            <div className="grid gap-6 lg:grid-cols-2">
              {QUADRANTS.map((quadrant) => (
                <QuadrantColumn
                  key={quadrant.id}
                  quadrant={quadrant}
                  tasks={groups[quadrant.id]}
                  busyTaskId={busyTaskId}
                  onApprove={approveProposal}
                  onCancel={cancelProposal}
                  onClassify={classify}
                  proposalsByTask={proposalsByTask}
                />
              ))}
            </div>
          )}
        </main>
      </DndContext>

      <BrainDumpModal
        loading={creating}
        onClose={() => setBrainDumpOpen(false)}
        onSubmit={brainDump}
        open={brainDumpOpen}
      />
    </div>
  );
}