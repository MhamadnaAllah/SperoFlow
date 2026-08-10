"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useDroppable } from "@dnd-kit/core";

import { ApiError, calendarApi, tasksApi } from "@/lib/api/client";

// ─── Layout & Time Constants ──────────────────────────────────────────────────
const HOUR_HEIGHT = 70;
const SNAP_MINUTES = 15;
const DAYS = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
const HOURS = Array.from({ length: 24 }, (_, i) => i);
const MONTHS = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

const COLOR_MAP = {
  indigo: { bg: "#eef2ff", text: "#3730a3", border: "#4f46e5" },
  blue: { bg: "#eff6ff", text: "#1e3a8a", border: "#2563eb" },
  emerald: { bg: "#ecfdf5", text: "#065f46", border: "#059669" },
  rose: { bg: "#fff1f2", text: "#9f1239", border: "#e11d48" },
  amber: { bg: "#fffbeb", text: "#78350f", border: "#d97706" },
  purple: { bg: "#faf5ff", text: "#581c87", border: "#7c3aed" },
  teal: { bg: "#f0fdfa", text: "#134e4a", border: "#0d9488" },
};
const COLOR_KEYS = Object.keys(COLOR_MAP);

function messageFrom(error, fallback) {
  return error instanceof ApiError ? error.message : fallback;
}

function toDateStr(date) {
  if (!date) return "";
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, "0");
  const d = String(date.getDate()).padStart(2, "0");
  return `${y}-${m}-${d}`;
}

function formatHour(h) {
  if (h === 0) return "12 AM";
  if (h < 12) return `${h} AM`;
  if (h === 12) return "12 PM";
  return `${h - 12} PM`;
}

function fmtMinutes(m) {
  const h = Math.floor(m / 60) % 24;
  const min = m % 60;
  const ampm = h < 12 ? "AM" : "PM";
  return `${h % 12 || 12}:${min.toString().padStart(2, "0")} ${ampm}`;
}

function formatTimeRange(s, e) {
  return `${fmtMinutes(s)} – ${fmtMinutes(e)}`;
}

function getWeekDates(base) {
  const d = new Date(base);
  d.setDate(d.getDate() - d.getDay());
  d.setHours(0, 0, 0, 0);
  return Array.from({ length: 7 }, (_, i) => {
    const dd = new Date(d);
    dd.setDate(d.getDate() + i);
    return dd;
  });
}

function getMonthDates(base) {
  const y = base.getFullYear();
  const m = base.getMonth();
  const first = new Date(y, m, 1);
  const last = new Date(y, m + 1, 0);
  const startDay = first.getDay();
  const cells = [];
  for (let i = 0; i < startDay; i++) {
    cells.push({ date: new Date(y, m, -(startDay - 1 - i)), outside: true });
  }
  for (let d = 1; d <= last.getDate(); d++) {
    cells.push({ date: new Date(y, m, d), outside: false });
  }
  while (cells.length < 42) {
    cells.push({ date: new Date(y, m + 1, cells.length - startDay - last.getDate() + 1), outside: true });
  }
  return cells;
}

// Convert API response (startsAt/endsAt ISO strings) to component event format
function apiToLocal(item) {
  const start = new Date(item.startsAt);
  const end = new Date(item.endsAt);
  const durationMin = Math.max(15, Math.round((end.getTime() - start.getTime()) / 60000));
  return {
    id: item.id,
    dateStr: toDateStr(start),
    startMin: start.getHours() * 60 + start.getMinutes(),
    durationMin,
    title: item.title,
    color: item.color || "indigo",
    role: item.role || "",
    concurrencyToken: item.concurrencyToken,
  };
}

// Convert local event to API payload (startsAt/endsAt ISO strings)
function localToApi(evt) {
  const [y, m, d] = evt.dateStr.split("-").map(Number);
  const start = new Date(y, m - 1, d, Math.floor(evt.startMin / 60), evt.startMin % 60, 0, 0);
  const end = new Date(start.getTime() + evt.durationMin * 60000);
  return {
    title: evt.title,
    startsAt: start.toISOString(),
    endsAt: end.toISOString(),
    color: evt.color || "indigo",
    role: evt.role || null,
  };
}

// ─── Add/Edit Event Modal ─────────────────────────────────────────────────────
function EventModal({ slot, eventToEdit, onSave, onDelete, onClose }) {
  const [title, setTitle] = useState(eventToEdit?.title || "");
  const [color, setColor] = useState(eventToEdit?.color || "indigo");
  const [duration, setDuration] = useState(eventToEdit?.durationMin || 60);

  const handleSave = () => {
    if (!title.trim()) return;
    onSave({
      ...(eventToEdit || {}),
      title: title.trim(),
      color,
      durationMin: duration,
      dateStr: slot ? slot.dateStr : eventToEdit.dateStr,
      startMin: slot ? slot.startMin : eventToEdit.startMin,
    });
    onClose();
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4"
      style={{ backgroundColor: "rgba(0,0,0,0.45)", backdropFilter: "blur(4px)" }}
      onClick={(e) => e.target === e.currentTarget && onClose()}
    >
      <div className="w-full max-w-md rounded-3xl border border-outline-variant/30 bg-white p-6 shadow-2xl animate-in fade-in zoom-in-95">
        <div className="mb-4 flex items-center justify-between">
          <h2 className="text-lg font-bold text-slate-800">
            {eventToEdit ? "Edit Calendar Event" : "Schedule Event"}
          </h2>
          <button
            onClick={onClose}
            className="flex h-8 w-8 items-center justify-center rounded-full text-slate-400 hover:bg-slate-100 hover:text-slate-700"
            type="button"
          >
            <span className="material-symbols-outlined text-sm">close</span>
          </button>
        </div>

        <div className="space-y-4">
          <div>
            <label className="mb-1 block text-xs font-bold uppercase tracking-wider text-slate-500">
              Event Title
            </label>
            <input
              type="text"
              autoFocus
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              placeholder="e.g. Deep Work: API Design"
              className="w-full rounded-xl border border-slate-200 bg-slate-50 px-4 py-2.5 text-sm text-slate-800 outline-none focus:border-primary focus:bg-white"
            />
          </div>

          <div>
            <label className="mb-1 block text-xs font-bold uppercase tracking-wider text-slate-500">
              Duration
            </label>
            <select
              value={duration}
              onChange={(e) => setDuration(Number(e.target.value))}
              className="w-full rounded-xl border border-slate-200 bg-slate-50 px-4 py-2.5 text-sm text-slate-800 outline-none focus:border-primary focus:bg-white"
            >
              <option value={15}>15 minutes</option>
              <option value={30}>30 minutes</option>
              <option value={45}>45 minutes</option>
              <option value={60}>1 hour</option>
              <option value={90}>1.5 hours</option>
              <option value={120}>2 hours</option>
              <option value={180}>3 hours</option>
            </select>
          </div>

          <div>
            <label className="mb-1 block text-xs font-bold uppercase tracking-wider text-slate-500">
              Color Tag
            </label>
            <div className="flex gap-2">
              {COLOR_KEYS.map((k) => {
                const c = COLOR_MAP[k];
                return (
                  <button
                    key={k}
                    type="button"
                    onClick={() => setColor(k)}
                    className={`h-7 w-7 rounded-full transition-transform ${color === k ? "scale-125 ring-2 ring-primary ring-offset-2" : "hover:scale-110"}`}
                    style={{ backgroundColor: c.border }}
                  />
                );
              })}
            </div>
          </div>

          <div className="flex items-center justify-between pt-4">
            {eventToEdit ? (
              <button
                type="button"
                onClick={() => {
                  onDelete(eventToEdit.id);
                  onClose();
                }}
                className="flex items-center gap-1 text-xs font-bold text-red-500 hover:text-red-700"
              >
                <span className="material-symbols-outlined text-sm">delete</span> Delete
              </button>
            ) : <div />}

            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={onClose}
                className="rounded-xl px-4 py-2 text-sm font-bold text-slate-500 hover:bg-slate-100"
              >
                Cancel
              </button>
              <button
                type="button"
                disabled={!title.trim()}
                onClick={handleSave}
                className="rounded-xl bg-primary px-5 py-2 text-sm font-bold text-white shadow-sm hover:bg-primary/90 disabled:opacity-50"
              >
                Save
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

// ─── Droppable Calendar Time Slot ─────────────────────────────────────────────
function CalendarDaySlot({ dateStr, hour, onSlotClick }) {
  const startMin = hour * 60;
  const { isOver, setNodeRef } = useDroppable({
    id: `calendar-slot-${dateStr}-${hour}`,
    data: { type: "calendar-day", dateStr, startMin },
  });

  return (
    <div
      ref={setNodeRef}
      onClick={() => onSlotClick(dateStr, startMin)}
      className={`group relative h-[70px] border-b border-r border-slate-100 transition-colors ${
        isOver ? "bg-primary/10 ring-2 ring-primary/40 inset-0 z-10" : "hover:bg-slate-50/70"
      }`}
    >
      <span className="absolute left-1 top-1 hidden text-[9px] font-semibold text-slate-300 group-hover:inline">
        + Add
      </span>
    </div>
  );
}

// ─── Main AgenticHubView Calendar Component ───────────────────────────────────
export default function AgenticHubView() {
  const [viewMode, setViewMode] = useState("week"); // "week" (main) or "month"
  const [baseDate, setBaseDate] = useState(new Date());
  const [events, setEvents] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [activeSlot, setActiveSlot] = useState(null);
  const [editingEvent, setEditingEvent] = useState(null);

  const scrollRef = useRef(null);
  const weekDates = useMemo(() => getWeekDates(baseDate), [baseDate]);
  const monthCells = useMemo(() => getMonthDates(baseDate), [baseDate]);

  // Load calendar events from backend API
  const loadEvents = useCallback(async () => {
    setLoading(true);
    try {
      const items = await calendarApi.list();
      setEvents((items || []).map(apiToLocal));
      setError(null);
    } catch (err) {
      setError(messageFrom(err, "Unable to load calendar events."));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadEvents();
  }, [loadEvents]);

  // Handle Drag-and-Drop from Sidebar to Calendar Slot
  useEffect(() => {
    window.__calendarDragEnd = async (event) => {
      const { active, over } = event;
      if (!over || over.data.current?.type !== "calendar-day") return;

      const taskData = active.data.current;
      const { dateStr, startMin } = over.data.current;
      const title = taskData.title || taskData.name || "Scheduled Task";
      const durationMin = taskData.estimatedMinutes || 45;

      const localEvt = {
        title,
        color: "indigo",
        durationMin,
        dateStr,
        startMin,
      };

      try {
        const created = await calendarApi.create(localToApi(localEvt));
        setEvents((prev) => [...prev, apiToLocal(created)]);
      } catch (err) {
        setError(messageFrom(err, "Failed to schedule task onto calendar."));
      }
    };

    return () => {
      delete window.__calendarDragEnd;
    };
  }, []);

  // Scroll to current hour on week view mount
  useEffect(() => {
    if (viewMode === "week" && scrollRef.current) {
      const currentHour = new Date().getHours();
      scrollRef.current.scrollTop = Math.max(0, (currentHour - 1) * HOUR_HEIGHT);
    }
  }, [viewMode]);

  const handlePrev = () => {
    const d = new Date(baseDate);
    if (viewMode === "week") d.setDate(d.getDate() - 7);
    else d.setMonth(d.getMonth() - 1);
    setBaseDate(d);
  };

  const handleNext = () => {
    const d = new Date(baseDate);
    if (viewMode === "week") d.setDate(d.getDate() + 7);
    else d.setMonth(d.getMonth() + 1);
    setBaseDate(d);
  };

  const handleToday = () => setBaseDate(new Date());

  const handleSaveEvent = async (evtData) => {
    try {
      if (evtData.id) {
        const payload = {
          ...localToApi(evtData),
          concurrencyToken: evtData.concurrencyToken,
        };
        const updated = await calendarApi.update(evtData.id, payload);
        setEvents((prev) => prev.map((e) => (e.id === evtData.id ? apiToLocal(updated) : e)));
      } else {
        const created = await calendarApi.create(localToApi(evtData));
        setEvents((prev) => [...prev, apiToLocal(created)]);
      }
    } catch (err) {
      setError(messageFrom(err, "Failed to save calendar event."));
    }
  };

  const handleDeleteEvent = async (id) => {
    try {
      await calendarApi.remove(id);
      setEvents((prev) => prev.filter((e) => e.id !== id));
    } catch (err) {
      setError(messageFrom(err, "Failed to delete calendar event."));
    }
  };

  const todayStr = toDateStr(new Date());

  return (
    <div className="flex h-full flex-col bg-surface overflow-hidden">
      {/* Calendar Header */}
      <header className="flex flex-wrap items-center justify-between gap-4 border-b border-slate-200/80 bg-white/80 px-6 py-4 backdrop-blur-xl">
        <div className="flex items-center gap-3">
          <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-primary/10 text-primary">
            <span className="material-symbols-outlined" style={{ fontVariationSettings: "'FILL' 1" }}>
              calendar_month
            </span>
          </div>
          <div>
            <h1 className="text-xl font-bold tracking-tight text-slate-800">
              {MONTHS[baseDate.getMonth()]} {baseDate.getFullYear()}
            </h1>
            <p className="text-xs font-semibold text-slate-400">
              {viewMode === "week" ? "Weekly Execution Plan" : "Monthly Strategy Overview"}
            </p>
          </div>
        </div>

        {/* View Toggle & Navigation */}
        <div className="flex items-center gap-3">
          <div className="flex rounded-xl bg-slate-100 p-1">
            <button
              onClick={() => setViewMode("week")}
              className={`rounded-lg px-3 py-1.5 text-xs font-bold transition-all ${
                viewMode === "week" ? "bg-white text-primary shadow-sm" : "text-slate-500 hover:text-slate-800"
              }`}
              type="button"
            >
              Weekly
            </button>
            <button
              onClick={() => setViewMode("month")}
              className={`rounded-lg px-3 py-1.5 text-xs font-bold transition-all ${
                viewMode === "month" ? "bg-white text-primary shadow-sm" : "text-slate-500 hover:text-slate-800"
              }`}
              type="button"
            >
              Monthly
            </button>
          </div>

          <div className="flex items-center gap-1 rounded-xl border border-slate-200 bg-white p-1 shadow-sm">
            <button
              onClick={handlePrev}
              className="flex h-7 w-7 items-center justify-center rounded-lg text-slate-500 hover:bg-slate-100"
              type="button"
            >
              <span className="material-symbols-outlined text-sm">chevron_left</span>
            </button>
            <button
              onClick={handleToday}
              className="px-2 py-1 text-xs font-bold text-slate-600 hover:text-primary"
              type="button"
            >
              Today
            </button>
            <button
              onClick={handleNext}
              className="flex h-7 w-7 items-center justify-center rounded-lg text-slate-500 hover:bg-slate-100"
              type="button"
            >
              <span className="material-symbols-outlined text-sm">chevron_right</span>
            </button>
          </div>
        </div>
      </header>

      {/* Error Banner */}
      {error && (
        <div className="flex items-center justify-between border-b border-red-200 bg-red-50 px-6 py-2 text-xs font-semibold text-red-700">
          <span>{error}</span>
          <button onClick={() => setError(null)} className="underline">
            Dismiss
          </button>
        </div>
      )}

      {/* ─── WEEKLY VIEW (MAIN) ─────────────────────────────────────────────── */}
      {viewMode === "week" && (
        <div className="flex flex-1 flex-col overflow-hidden">
          {/* Weekday Column Headers */}
          <div className="grid grid-cols-[60px_repeat(7,1fr)] border-b border-slate-200 bg-slate-50/80 pr-4 text-center">
            <div className="py-3 text-xs font-bold text-slate-400">GMT</div>
            {weekDates.map((d) => {
              const dStr = toDateStr(d);
              const isToday = dStr === todayStr;
              return (
                <div key={dStr} className="py-2.5 border-l border-slate-200/60">
                  <span className={`block text-[10px] font-extrabold uppercase ${isToday ? "text-primary" : "text-slate-400"}`}>
                    {DAYS[d.getDay()]}
                  </span>
                  <span
                    className={`inline-flex h-7 w-7 items-center justify-center rounded-full text-xs font-bold ${
                      isToday ? "bg-primary text-white shadow-sm" : "text-slate-700"
                    }`}
                  >
                    {d.getDate()}
                  </span>
                </div>
              );
            })}
          </div>

          {/* Time Grid View */}
          <div ref={scrollRef} className="flex-1 overflow-y-auto sidebar-scroll">
            <div className="relative grid grid-cols-[60px_repeat(7,1fr)]">
              {/* Hour Labels */}
              <div className="flex flex-col border-r border-slate-200 bg-slate-50/50">
                {HOURS.map((h) => (
                  <div
                    key={h}
                    className="flex h-[70px] items-start justify-end pr-2 pt-1 text-[10px] font-bold text-slate-400"
                  >
                    {formatHour(h)}
                  </div>
                ))}
              </div>

              {/* 7-Day Time Slot Columns */}
              {weekDates.map((d) => {
                const dStr = toDateStr(d);
                const dayEvents = events.filter((e) => e.dateStr === dStr);

                return (
                  <div key={dStr} className="relative border-l border-slate-200/60">
                    {HOURS.map((h) => (
                      <CalendarDaySlot
                        key={h}
                        dateStr={dStr}
                        hour={h}
                        onSlotClick={(dateStr, startMin) => setActiveSlot({ dateStr, startMin })}
                      />
                    ))}

                    {/* Render Event Overlay Blocks */}
                    {dayEvents.map((evt) => {
                      const colorStyle = COLOR_MAP[evt.color] || COLOR_MAP.indigo;
                      const top = (evt.startMin / 60) * HOUR_HEIGHT;
                      const height = Math.max(25, (evt.durationMin / 60) * HOUR_HEIGHT);

                      return (
                        <div
                          key={evt.id}
                          onClick={(e) => {
                            e.stopPropagation();
                            setEditingEvent(evt);
                          }}
                          className="absolute left-1 right-1 rounded-xl p-2 shadow-sm transition-all hover:shadow-md cursor-pointer border-l-4 overflow-hidden z-20"
                          style={{
                            top: `${top}px`,
                            height: `${height}px`,
                            backgroundColor: colorStyle.bg,
                            borderLeftColor: colorStyle.border,
                            color: colorStyle.text,
                          }}
                        >
                          <p className="truncate text-xs font-bold leading-tight">{evt.title}</p>
                          <p className="mt-0.5 text-[9px] font-semibold opacity-80">
                            {formatTimeRange(evt.startMin, evt.startMin + evt.durationMin)}
                          </p>
                        </div>
                      );
                    })}
                  </div>
                );
              })}
            </div>
          </div>
        </div>
      )}

      {/* ─── MONTHLY VIEW ───────────────────────────────────────────────────── */}
      {viewMode === "month" && (
        <div className="flex flex-1 flex-col overflow-hidden p-6">
          <div className="grid grid-cols-7 border-b border-slate-200 pb-2 text-center text-xs font-bold text-slate-400">
            {DAYS.map((day) => (
              <div key={day}>{day}</div>
            ))}
          </div>

          <div className="grid flex-1 grid-cols-7 grid-rows-6 gap-1 pt-2">
            {monthCells.map(({ date, outside }, idx) => {
              const dStr = toDateStr(date);
              const isToday = dStr === todayStr;
              const cellEvents = events.filter((e) => e.dateStr === dStr);

              return (
                <div
                  key={idx}
                  onClick={() => {
                    setViewMode("week");
                    setBaseDate(date);
                  }}
                  className={`flex flex-col rounded-2xl border p-2 transition-all cursor-pointer ${
                    outside
                      ? "bg-slate-50/50 border-slate-100 text-slate-300"
                      : isToday
                      ? "bg-primary/5 border-primary/40 text-slate-800 shadow-sm"
                      : "bg-white border-slate-100 hover:border-slate-300 text-slate-700"
                  }`}
                >
                  <span className={`text-xs font-bold ${isToday ? "text-primary" : ""}`}>
                    {date.getDate()}
                  </span>

                  <div className="mt-1 space-y-1 overflow-y-auto sidebar-scroll">
                    {cellEvents.slice(0, 3).map((evt) => {
                      const c = COLOR_MAP[evt.color] || COLOR_MAP.indigo;
                      return (
                        <div
                          key={evt.id}
                          className="truncate rounded-md px-1.5 py-0.5 text-[9px] font-bold"
                          style={{ backgroundColor: c.bg, color: c.text }}
                        >
                          {evt.title}
                        </div>
                      );
                    })}
                    {cellEvents.length > 3 && (
                      <span className="block text-[9px] font-bold text-slate-400">
                        +{cellEvents.length - 3} more
                      </span>
                    )}
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      )}

      {/* Modal for Creating / Editing Event */}
      {(activeSlot || editingEvent) && (
        <EventModal
          slot={activeSlot}
          eventToEdit={editingEvent}
          onSave={handleSaveEvent}
          onDelete={handleDeleteEvent}
          onClose={() => {
            setActiveSlot(null);
            setEditingEvent(null);
          }}
        />
      )}
    </div>
  );
}