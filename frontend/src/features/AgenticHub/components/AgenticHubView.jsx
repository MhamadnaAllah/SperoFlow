"use client";

import { useCallback, useEffect, useMemo, useState } from "react";

import { aiApi, aiProposalsApi, ApiError, calendarApi, tasksApi } from "@/lib/api/client";

const EVENT_COLORS = ["#0053dc", "#006d4a", "#865400", "#7c3aed", "#b42318"];
const WEEKDAYS = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
const SCHEDULE_PROPOSAL_KIND = "applyTaskSchedule";

function messageFrom(error, fallback) {
  return error instanceof ApiError ? error.message : fallback;
}

function dateKey(date) {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
}

function toDateTime(value) {
  return value ? new Date(value).toISOString() : null;
}

function startOfMonth(date) {
  return new Date(date.getFullYear(), date.getMonth(), 1);
}

function endOfMonth(date) {
  return new Date(date.getFullYear(), date.getMonth() + 1, 0);
}

function calendarDays(month) {
  const first = startOfMonth(month);
  const start = new Date(first);
  start.setDate(start.getDate() - first.getDay());
  return Array.from({ length: 42 }, (_, index) => {
    const day = new Date(start);
    day.setDate(start.getDate() + index);
    return day;
  });
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

function formatDateTime(value) {
  return new Date(value).toLocaleString(undefined, {
    weekday: "short",
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
  });
}

function formatDueDate(value) {
  return new Date(value).toLocaleDateString(undefined, { month: "short", day: "numeric" });
}

function scheduleDetails(proposal) {
  if (proposal?.kind !== SCHEDULE_PROPOSAL_KIND || !proposal.payload || typeof proposal.payload !== "object") {
    return null;
  }

  const { taskId, startAt, endAt, durationMinutes } = proposal.payload;
  if (typeof taskId !== "string" || typeof startAt !== "string" || typeof endAt !== "string" || !Number.isInteger(durationMinutes)) {
    return null;
  }

  return { taskId, startAt, endAt, durationMinutes };
}

function IconButton({ label, icon, onClick, disabled = false, tone = "default" }) {
  const toneClass = tone === "primary"
    ? "bg-primary text-on-primary hover:bg-primary/90"
    : "text-on-surface-variant hover:bg-surface-container";
  return (
    <button
      aria-label={label}
      className={`flex h-8 w-8 shrink-0 items-center justify-center rounded-lg disabled:cursor-not-allowed disabled:opacity-45 ${toneClass}`}
      disabled={disabled}
      onClick={onClick}
      title={label}
      type="button"
    >
      <span className="material-symbols-outlined" style={{ fontSize: "18px" }}>{icon}</span>
    </button>
  );
}

function EventModal({ date, onClose, onSave, saving }) {
  const day = dateKey(date);
  const [form, setForm] = useState({
    title: "",
    startsAt: `${day}T09:00`,
    endsAt: `${day}T10:00`,
    color: EVENT_COLORS[0],
    role: "",
  });
  const change = (key, value) => setForm((current) => ({ ...current, [key]: value }));

  return (
    <div className="fixed inset-0 z-[70] flex items-center justify-center bg-slate-950/35 p-4" onMouseDown={onClose}>
      <form
        aria-modal="true"
        className="w-full max-w-md rounded-lg border border-outline-variant/30 bg-white shadow-2xl"
        onMouseDown={(event) => event.stopPropagation()}
        onSubmit={(event) => {
          event.preventDefault();
          if (!form.title.trim()) return;
          onSave({
            title: form.title.trim(),
            startsAt: toDateTime(form.startsAt),
            endsAt: toDateTime(form.endsAt),
            color: form.color,
            role: form.role.trim() || null,
          });
        }}
      >
        <header className="flex items-center justify-between border-b border-outline-variant/20 px-5 py-4">
          <h2 className="text-lg font-bold text-on-surface">New calendar event</h2>
          <IconButton icon="close" label="Close" onClick={onClose} />
        </header>
        <div className="space-y-4 p-5">
          <label className="block text-sm font-semibold text-on-surface">
            Title
            <input autoFocus className="mt-1.5 w-full rounded-lg border border-outline-variant/40 bg-surface px-3 py-2.5 text-sm font-normal" onChange={(event) => change("title", event.target.value)} value={form.title} />
          </label>
          <div className="grid gap-4 sm:grid-cols-2">
            <label className="block text-sm font-semibold text-on-surface">
              Starts
              <input className="mt-1.5 w-full rounded-lg border border-outline-variant/40 bg-surface px-3 py-2.5 text-sm font-normal" onChange={(event) => change("startsAt", event.target.value)} type="datetime-local" value={form.startsAt} />
            </label>
            <label className="block text-sm font-semibold text-on-surface">
              Ends
              <input className="mt-1.5 w-full rounded-lg border border-outline-variant/40 bg-surface px-3 py-2.5 text-sm font-normal" onChange={(event) => change("endsAt", event.target.value)} type="datetime-local" value={form.endsAt} />
            </label>
          </div>
          <label className="block text-sm font-semibold text-on-surface">
            Role or context
            <input className="mt-1.5 w-full rounded-lg border border-outline-variant/40 bg-surface px-3 py-2.5 text-sm font-normal" onChange={(event) => change("role", event.target.value)} value={form.role} />
          </label>
          <div className="flex gap-2" role="group" aria-label="Event color">
            {EVENT_COLORS.map((color) => (
              <button
                aria-label={`Use ${color}`}
                aria-pressed={form.color === color}
                className={`h-8 w-8 rounded-full border-2 ${form.color === color ? "border-on-surface" : "border-transparent"}`}
                key={color}
                onClick={() => change("color", color)}
                style={{ background: color }}
                type="button"
              />
            ))}
          </div>
        </div>
        <footer className="flex justify-end gap-3 border-t border-outline-variant/20 px-5 py-4">
          <button className="rounded-lg px-4 py-2 text-sm font-semibold text-on-surface-variant hover:bg-surface-container" onClick={onClose} type="button">Cancel</button>
          <button className="rounded-lg bg-primary px-4 py-2 text-sm font-bold text-on-primary disabled:opacity-50" disabled={saving || !form.title.trim()} type="submit">{saving ? "Adding" : "Add event"}</button>
        </footer>
      </form>
    </div>
  );
}

function ScheduleModal({ task, onClose, onSave, saving }) {
  const tomorrow = new Date();
  tomorrow.setDate(tomorrow.getDate() + 1);
  const [targetDate, setTargetDate] = useState(dateKey(tomorrow));
  const [durationMinutes, setDurationMinutes] = useState(task.estimatedMinutes || 30);
  const durationIsValid = Number.isInteger(Number(durationMinutes)) && Number(durationMinutes) >= 5 && Number(durationMinutes) <= 480;

  return (
    <div className="fixed inset-0 z-[70] flex items-center justify-center bg-slate-950/35 p-4" onMouseDown={onClose}>
      <form
        aria-modal="true"
        className="w-full max-w-md rounded-lg border border-outline-variant/30 bg-white shadow-2xl"
        onMouseDown={(event) => event.stopPropagation()}
        onSubmit={(event) => {
          event.preventDefault();
          if (!durationIsValid) return;
          onSave({ targetDate, durationMinutes: Number(durationMinutes) });
        }}
      >
        <header className="flex items-center justify-between border-b border-outline-variant/20 px-5 py-4">
          <div className="min-w-0">
            <p className="text-xs font-bold uppercase text-secondary">Focus block</p>
            <h2 className="truncate text-lg font-bold text-on-surface">{task.title}</h2>
          </div>
          <IconButton icon="close" label="Close" onClick={onClose} />
        </header>
        <div className="space-y-4 p-5">
          <label className="block text-sm font-semibold text-on-surface">
            Date
            <input className="mt-1.5 w-full rounded-lg border border-outline-variant/40 bg-surface px-3 py-2.5 text-sm font-normal" min={dateKey(new Date())} onChange={(event) => setTargetDate(event.target.value)} required type="date" value={targetDate} />
          </label>
          <label className="block text-sm font-semibold text-on-surface">
            Minutes
            <input className="mt-1.5 w-full rounded-lg border border-outline-variant/40 bg-surface px-3 py-2.5 text-sm font-normal" max="480" min="5" onChange={(event) => setDurationMinutes(event.target.value)} required type="number" value={durationMinutes} />
          </label>
        </div>
        <footer className="flex justify-end gap-3 border-t border-outline-variant/20 px-5 py-4">
          <button className="rounded-lg px-4 py-2 text-sm font-semibold text-on-surface-variant hover:bg-surface-container" onClick={onClose} type="button">Cancel</button>
          <button className="rounded-lg bg-primary px-4 py-2 text-sm font-bold text-on-primary disabled:opacity-50" disabled={saving || !durationIsValid} type="submit">{saving ? "Preparing" : "Prepare suggestion"}</button>
        </footer>
      </form>
    </div>
  );
}

function ScheduleReview({ proposal, task, onApprove, onCancel, saving }) {
  const schedule = scheduleDetails(proposal);
  if (!schedule) return null;

  return (
    <article className="rounded-lg border border-secondary/25 bg-secondary/5 p-3" id={`schedule-${proposal.id}`}>
      <div className="flex gap-2.5">
        <span className="material-symbols-outlined mt-0.5 text-secondary" style={{ fontSize: "18px" }}>event_available</span>
        <div className="min-w-0 flex-1">
          <p className="truncate text-xs font-bold text-on-surface">{task?.title || proposal.title.replace(/^Schedule:\s*/, "")}</p>
          <p className="mt-1 text-[11px] font-semibold text-secondary">{formatDateTime(schedule.startAt)} for {schedule.durationMinutes} min</p>
          <p className="mt-1.5 text-[11px] leading-4 text-on-surface-variant">{proposal.description}</p>
        </div>
      </div>
      <div className="mt-3 flex justify-end gap-2">
        <button className="rounded-lg px-3 py-1.5 text-xs font-semibold text-on-surface-variant hover:bg-white" disabled={saving} onClick={() => onCancel(proposal)} type="button">Dismiss</button>
        <button className="rounded-lg bg-primary px-3 py-1.5 text-xs font-bold text-on-primary disabled:opacity-50" disabled={saving} onClick={() => onApprove(proposal)} type="button">Approve</button>
      </div>
    </article>
  );
}

export default function AgenticHubView() {
  const [month, setMonth] = useState(() => startOfMonth(new Date()));
  const [events, setEvents] = useState([]);
  const [tasks, setTasks] = useState([]);
  const [proposals, setProposals] = useState([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [modalDate, setModalDate] = useState(null);
  const [scheduleTask, setScheduleTask] = useState(null);
  const [notice, setNotice] = useState(null);
  const days = useMemo(() => calendarDays(month), [month]);
  const monthStartIso = useMemo(() => startOfMonth(month).toISOString(), [month]);
  const monthEndIso = useMemo(() => {
    const lastDay = endOfMonth(month);
    return new Date(lastDay.getFullYear(), lastDay.getMonth(), lastDay.getDate(), 23, 59, 59, 999).toISOString();
  }, [month]);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [nextEvents, nextTasks, nextProposals] = await Promise.all([
        calendarApi.list({ start: monthStartIso, end: monthEndIso }),
        tasksApi.list(),
        aiProposalsApi.list(),
      ]);
      setEvents(nextEvents);
      setTasks(nextTasks);
      setProposals(nextProposals);
      setNotice(null);
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "Unable to load the calendar.") });
    } finally {
      setLoading(false);
    }
  }, [monthEndIso, monthStartIso]);

  useEffect(() => {
    void load();
  }, [load]);

  const eventsByDate = useMemo(() => events.reduce((all, event) => {
    const key = dateKey(new Date(event.startsAt));
    (all[key] ||= []).push(event);
    return all;
  }, {}), [events]);
  const tasksByDate = useMemo(() => tasks.reduce((all, task) => {
    const relevantDate = task.startAt || task.dueAt;
    if (!relevantDate) return all;
    const key = dateKey(new Date(relevantDate));
    (all[key] ||= []).push(task);
    return all;
  }, {}), [tasks]);
  const activeTasks = useMemo(() => tasks.filter((task) => task.state !== "completed" && task.state !== "cancelled"), [tasks]);
  const unscheduledTasks = useMemo(() => activeTasks.filter((task) => !task.startAt), [activeTasks]);
  const taskById = useMemo(() => new Map(tasks.map((task) => [task.id, task])), [tasks]);
  const scheduleProposals = useMemo(() => proposals
    .filter((proposal) => proposal.kind === SCHEDULE_PROPOSAL_KIND && proposal.state === "pending")
    .map((proposal) => ({ proposal, schedule: scheduleDetails(proposal) }))
    .filter((item) => item.schedule), [proposals]);
  const scheduleProposalByTaskId = useMemo(() => new Map(scheduleProposals.map((item) => [item.schedule.taskId, item.proposal])), [scheduleProposals]);

  const addEvent = async (input) => {
    setSaving(true);
    try {
      const event = await calendarApi.create(input);
      setEvents((current) => [...current, event]);
      setModalDate(null);
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "The event could not be added.") });
    } finally {
      setSaving(false);
    }
  };

  const removeEvent = async (event) => {
    try {
      await calendarApi.remove(event.id);
      setEvents((current) => current.filter((item) => item.id !== event.id));
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "The event could not be removed.") });
    }
  };

  const completeTask = async (task) => {
    try {
      const saved = await tasksApi.update(task.id, taskUpdate(task, { state: task.state === "completed" ? "todo" : "completed" }));
      setTasks((current) => current.map((item) => item.id === task.id ? saved : item));
      setProposals((current) => current.filter((proposal) => scheduleDetails(proposal)?.taskId !== task.id));
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "The task could not be updated.") });
    }
  };

  const proposeSchedule = async (input) => {
    if (!scheduleTask) return;
    setSaving(true);
    try {
      const proposal = await aiApi.proposeTaskSchedule(scheduleTask.id, input);
      setProposals((current) => [proposal, ...current.filter((item) => item.id !== proposal.id)]);
      setScheduleTask(null);
      setNotice({ type: "success", message: "A focus-block suggestion is ready for review." });
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "A schedule suggestion could not be prepared.") });
    } finally {
      setSaving(false);
    }
  };

  const approveSchedule = async (proposal) => {
    setSaving(true);
    try {
      await aiProposalsApi.approve(proposal.id, proposal.concurrencyToken);
      await load();
      setNotice({ type: "success", message: "The focus block was added to the calendar." });
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "The schedule suggestion could not be approved.") });
    } finally {
      setSaving(false);
    }
  };

  const cancelSchedule = async (proposal) => {
    setSaving(true);
    try {
      await aiProposalsApi.cancel(proposal.id, proposal.concurrencyToken);
      setProposals((current) => current.filter((item) => item.id !== proposal.id));
    } catch (error) {
      setNotice({ type: "error", message: messageFrom(error, "The schedule suggestion could not be dismissed.") });
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="mx-auto w-full max-w-[96rem] px-4 py-6 sm:px-6 sm:py-8 lg:px-8">
      <header className="flex flex-col gap-4 border-b border-outline-variant/20 pb-5 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <p className="text-xs font-bold uppercase text-secondary">Time</p>
          <h1 className="mt-1 text-3xl font-bold text-on-surface">Calendar</h1>
          <p className="mt-2 text-sm text-on-surface-variant">Events, deadlines, and approved focus blocks share one workspace.</p>
        </div>
        <button className="flex items-center justify-center gap-2 rounded-lg bg-primary px-4 py-2.5 text-sm font-bold text-on-primary" onClick={() => setModalDate(new Date())} type="button">
          <span className="material-symbols-outlined" style={{ fontSize: "18px" }}>add</span>
          New event
        </button>
      </header>

      {notice && (
        <div className={`mt-4 flex items-center justify-between gap-3 rounded-lg border px-4 py-3 text-sm font-medium ${notice.type === "error" ? "border-error/20 bg-error/10 text-error" : "border-secondary/20 bg-secondary/10 text-secondary"}`}>
          <span>{notice.message}</span>
          <button className="text-xs font-bold underline" onClick={() => setNotice(null)} type="button">Dismiss</button>
        </div>
      )}

      <main className="mt-5 grid gap-5 2xl:grid-cols-[minmax(0,1fr)_21rem]">
        <section className="overflow-hidden rounded-lg border border-outline-variant/25 bg-white shadow-sm">
          <header className="flex items-center justify-between border-b border-outline-variant/20 px-4 py-3">
            <IconButton icon="chevron_left" label="Previous month" onClick={() => setMonth((current) => new Date(current.getFullYear(), current.getMonth() - 1, 1))} />
            <h2 className="text-base font-bold text-on-surface">{month.toLocaleDateString(undefined, { month: "long", year: "numeric" })}</h2>
            <IconButton icon="chevron_right" label="Next month" onClick={() => setMonth((current) => new Date(current.getFullYear(), current.getMonth() + 1, 1))} />
          </header>
          <div className="grid grid-cols-7 border-b border-outline-variant/15">
            {WEEKDAYS.map((weekday) => <div className="px-2 py-2 text-center text-[11px] font-bold uppercase text-on-surface-variant" key={weekday}>{weekday}</div>)}
          </div>
          <div className="grid grid-cols-7">
            {days.map((day) => {
              const key = dateKey(day);
              const inMonth = day.getMonth() === month.getMonth();
              const isToday = key === dateKey(new Date());
              const dayEvents = eventsByDate[key] || [];
              const dayTasks = tasksByDate[key] || [];
              return (
                <div className={`min-h-32 border-b border-r border-outline-variant/15 p-1.5 sm:min-h-36 ${inMonth ? "bg-white" : "bg-surface-container-low/40"}`} key={key}>
                  <div className="flex items-center justify-between">
                    <button aria-label={`Add event on ${key}`} className={`flex h-6 w-6 items-center justify-center rounded-full text-xs font-bold ${isToday ? "bg-primary text-on-primary" : inMonth ? "text-on-surface hover:bg-surface-container" : "text-on-surface-variant/45"}`} onClick={() => setModalDate(day)} type="button">{day.getDate()}</button>
                    {inMonth && <IconButton icon="add" label={`Add event on ${key}`} onClick={() => setModalDate(day)} />}
                  </div>
                  {loading ? <div className="mt-3 h-5 animate-pulse rounded bg-surface-container" /> : (
                    <div className="mt-1 space-y-1">
                      {dayEvents.slice(0, 2).map((event) => (
                        <div className="group flex items-center gap-1 rounded px-1 py-0.5 text-[10px] font-bold text-white" key={event.id} style={{ background: event.color }}>
                          <span className="min-w-0 flex-1 truncate">{event.title}</span>
                          <button aria-label={`Remove ${event.title}`} className="hidden text-white/80 hover:text-white group-hover:block" onClick={() => removeEvent(event)} title={`Remove ${event.title}`} type="button">
                            <span className="material-symbols-outlined" style={{ fontSize: "13px" }}>close</span>
                          </button>
                        </div>
                      ))}
                      {dayTasks.slice(0, 2).map((task) => (
                        <button className={`block w-full truncate rounded bg-secondary/10 px-1 py-0.5 text-left text-[10px] font-bold text-secondary ${task.state === "completed" ? "line-through opacity-50" : ""}`} key={task.id} onClick={() => completeTask(task)} title={task.startAt ? "Mark task complete" : "Due task: mark complete"} type="button">
                          {task.startAt ? "Focus: " : "Due: "}{task.title}
                        </button>
                      ))}
                      {dayEvents.length + dayTasks.length > 2 && <p className="px-1 text-[10px] font-bold text-on-surface-variant">+{dayEvents.length + dayTasks.length - 2} more</p>}
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        </section>

        <aside className="space-y-5">
          <section className="rounded-lg border border-outline-variant/25 bg-white p-4 shadow-sm">
            <div className="flex items-center justify-between gap-3">
              <div>
                <h2 className="text-sm font-bold text-on-surface">Schedule review</h2>
                <p className="mt-1 text-xs text-on-surface-variant">{scheduleProposals.length} awaiting your decision</p>
              </div>
              <span className="material-symbols-outlined text-secondary" style={{ fontSize: "20px" }}>event_available</span>
            </div>
            <div className="mt-4 space-y-3">
              {scheduleProposals.map(({ proposal, schedule }) => (
                <ScheduleReview
                  key={proposal.id}
                  proposal={proposal}
                  saving={saving}
                  task={taskById.get(schedule.taskId)}
                  onApprove={approveSchedule}
                  onCancel={cancelSchedule}
                />
              ))}
              {!loading && scheduleProposals.length === 0 && <p className="py-5 text-center text-xs text-on-surface-variant">No schedule suggestions waiting for review.</p>}
            </div>
          </section>

          <section className="rounded-lg border border-outline-variant/25 bg-white p-4 shadow-sm">
            <div className="flex items-center justify-between gap-3">
              <div>
                <h2 className="text-sm font-bold text-on-surface">Unscheduled tasks</h2>
                <p className="mt-1 text-xs text-on-surface-variant">Tasks without a planned time.</p>
              </div>
              <span className="material-symbols-outlined text-on-surface-variant" style={{ fontSize: "20px" }}>checklist</span>
            </div>
            <div className="mt-4 space-y-2">
              {unscheduledTasks.map((task) => {
                const pendingProposal = scheduleProposalByTaskId.get(task.id);
                return (
                  <article className="flex items-start gap-2 rounded-lg border border-outline-variant/20 p-3" key={task.id}>
                    <IconButton icon="radio_button_unchecked" label={`Mark ${task.title} complete`} onClick={() => completeTask(task)} />
                    <div className="min-w-0 flex-1">
                      <p className="truncate text-xs font-bold text-on-surface">{task.title}</p>
                      <p className="mt-0.5 text-[11px] text-on-surface-variant">{task.lifeArea}{task.dueAt ? ` - Due ${formatDueDate(task.dueAt)}` : ""}</p>
                    </div>
                    <IconButton
                      disabled={saving}
                      icon={pendingProposal ? "pending_actions" : "event_available"}
                      label={pendingProposal ? `Review schedule suggestion for ${task.title}` : `Prepare a schedule suggestion for ${task.title}`}
                      onClick={() => pendingProposal ? document.getElementById(`schedule-${pendingProposal.id}`)?.scrollIntoView({ behavior: "smooth", block: "center" }) : setScheduleTask(task)}
                      tone={pendingProposal ? "primary" : "default"}
                    />
                  </article>
                );
              })}
              {!loading && unscheduledTasks.length === 0 && <p className="py-10 text-center text-xs text-on-surface-variant">No unscheduled tasks.</p>}
            </div>
          </section>
        </aside>
      </main>

      {modalDate && <EventModal date={modalDate} onClose={() => !saving && setModalDate(null)} onSave={addEvent} saving={saving} />}
      {scheduleTask && <ScheduleModal key={scheduleTask.id} onClose={() => !saving && setScheduleTask(null)} onSave={proposeSchedule} saving={saving} task={scheduleTask} />}
    </div>
  );
}