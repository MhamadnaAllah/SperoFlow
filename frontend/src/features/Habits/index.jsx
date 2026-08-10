"use client";

import { useCallback, useEffect, useMemo, useState } from "react";

import { ApiError, habitsApi } from "@/lib/api/client";
import AddHabitModal from "./components/AddHabitModal";

const DAY_ABBR = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

const LIFE_AREA_COLORS = {
  work: "#0053dc",
  family: "#e11d48",
  physical: "#dc2626",
  spiritual: "#865400",
  social: "#006d4a",
  learning: "#7c3aed",
  personal: "#4f46e5",
};

function titleCase(value) {
  return String(value || "").replace(/([A-Z])/g, " $1").replace(/^./, (character) => character.toUpperCase());
}

function localDateKey(date) {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
}

function getLast7Days() {
  return Array.from({ length: 7 }, (_, i) => {
    const d = new Date();
    d.setHours(12, 0, 0, 0);
    d.setDate(d.getDate() - (6 - i));
    return d;
  });
}

function calcStreak(checkInsSet, habitId, days) {
  let streak = 0;
  for (let i = days.length - 1; i >= 0; i--) {
    const key = `${habitId}:${localDateKey(days[i])}`;
    if (checkInsSet.has(key)) {
      streak++;
    } else {
      break;
    }
  }
  return streak;
}

// ─── HabitCard Component ──────────────────────────────────────────────────────
function HabitCard({ habit, checkInsSet, days, onToggle, onArchive, color }) {
  const todayKey = `${habit.id}:${localDateKey(days[6])}`;
  const completedToday = checkInsSet.has(todayKey);
  const streak = calcStreak(checkInsSet, habit.id, days);

  return (
    <div className="group flex flex-col rounded-2xl border border-outline-variant/30 bg-surface-container-lowest p-5 transition-all duration-300 hover:border-primary/20 hover:shadow-lg">
      <div className="mb-4 flex items-start justify-between">
        <div className="flex items-center gap-3">
          <div
            className="flex h-10 w-10 items-center justify-center rounded-xl shadow-sm"
            style={{ backgroundColor: `${color}15`, color: color }}
          >
            <span
              className="material-symbols-outlined"
              style={{ fontVariationSettings: "'FILL' 1" }}
            >
              rebase_edit
            </span>
          </div>
          <div>
            <h3 className="text-base font-bold text-on-surface">{habit.title}</h3>
            <p
              className="text-xs font-semibold uppercase tracking-wider opacity-70"
              style={{ color: color }}
            >
              {titleCase(habit.lifeArea || "daily")}
            </p>
          </div>
        </div>

        <div className="flex items-center gap-1.5">
          <button
            onClick={() => onArchive(habit)}
            className="flex h-7 w-7 items-center justify-center rounded-full text-transparent transition-all duration-200 group-hover:text-on-surface-variant/40 hover:bg-red-50 hover:!text-red-400"
            aria-label="Archive habit"
            title="Archive habit"
            type="button"
          >
            <span className="material-symbols-outlined text-[16px]">archive</span>
          </button>
          <button
            onClick={() => onToggle(habit, days[6])}
            className={`flex h-8 w-8 transform items-center justify-center rounded-full border-2 transition-all duration-300 active:scale-90 ${
              completedToday
                ? "bg-primary border-primary text-on-primary shadow-md"
                : "border-outline-variant text-transparent hover:border-primary/50 hover:text-primary/20"
            }`}
            aria-label={completedToday ? "Mark incomplete" : "Mark complete"}
            type="button"
          >
            <span className="material-symbols-outlined text-sm font-bold">check</span>
          </button>
        </div>
      </div>

      {habit.description && (
        <p className="mb-3 line-clamp-2 text-xs leading-relaxed text-on-surface-variant/70">
          {habit.description}
        </p>
      )}

      <div className="mt-auto flex items-center justify-between border-t border-outline-variant/20 pt-3">
        <div className="flex items-center gap-1.5">
          <span
            className="material-symbols-outlined text-amber-500"
            style={{ fontSize: "16px", fontVariationSettings: "'FILL' 1" }}
          >
            local_fire_department
          </span>
          <span className="text-sm font-bold text-on-surface-variant">
            {streak} Day Streak
          </span>
        </div>
        <div className="flex gap-1">
          {days.map((date, idx) => {
            const key = `${habit.id}:${localDateKey(date)}`;
            const completed = checkInsSet.has(key);
            return (
              <div
                key={idx}
                className={`h-8 w-3 rounded-full transition-all duration-300 ${completed ? "opacity-100" : "opacity-20"}`}
                style={{ backgroundColor: color }}
                title={`${DAY_ABBR[date.getDay()]} ${date.getDate()}/${date.getMonth() + 1}: ${completed ? "Completed" : "Missed"}`}
              />
            );
          })}
        </div>
      </div>
    </div>
  );
}

// ─── HabitsView Main Component ────────────────────────────────────────────────
export default function HabitsView() {
  const days = useMemo(getLast7Days, []);
  const [habits, setHabits] = useState([]);
  const [checkIns, setCheckIns] = useState([]);
  const [loading, setLoading] = useState(true);
  const [showModal, setShowModal] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [nextHabits, nextCheckIns] = await Promise.all([
        habitsApi.list(),
        habitsApi.listCheckIns({ from: localDateKey(days[0]), to: localDateKey(days[days.length - 1]) }),
      ]);
      setHabits(nextHabits);
      setCheckIns(nextCheckIns);
      setError(null);
    } catch (requestError) {
      setError(requestError instanceof ApiError ? requestError.message : "Unable to load habits.");
    } finally {
      setLoading(false);
    }
  }, [days]);

  useEffect(() => {
    void load();
  }, [load]);

  const checkInsSet = useMemo(
    () => new Set(checkIns.map((checkIn) => `${checkIn.habitId}:${checkIn.occurredOn}`)),
    [checkIns]
  );

  const completedToday = habits.filter((h) => checkInsSet.has(`${h.id}:${localDateKey(days[6])}`)).length;
  const progressPercent = habits.length ? Math.round((completedToday / habits.length) * 100) : 0;

  const toggle = async (habit, date) => {
    const occurredOn = localDateKey(date);
    const key = `${habit.id}:${occurredOn}`;
    try {
      if (checkInsSet.has(key)) {
        await habitsApi.removeCheckIn(habit.id, occurredOn);
        setCheckIns((current) => current.filter((c) => !(c.habitId === habit.id && c.occurredOn === occurredOn)));
      } else {
        const created = await habitsApi.addCheckIn(habit.id, { occurredOn, note: null });
        setCheckIns((current) => [...current, created]);
      }
    } catch (requestError) {
      setError(requestError instanceof ApiError ? requestError.message : "Unable to update this check-in.");
    }
  };

  const createHabit = async (input) => {
    setSaving(true);
    try {
      const habit = await habitsApi.create({
        title: input.name,
        description: input.description || null,
        lifeArea: input.frequency === "weekly" ? "social" : "personal",
        targetPerWeek: input.frequency === "weekly" ? 1 : 7,
      });
      setHabits((current) => [...current, habit]);
      setShowModal(false);
    } catch (requestError) {
      setError(requestError instanceof ApiError ? requestError.message : "Unable to create habit.");
    } finally {
      setSaving(false);
    }
  };

  const archiveHabit = async (habit) => {
    try {
      await habitsApi.archive(habit.id, habit.concurrencyToken);
      setHabits((current) => current.filter((item) => item.id !== habit.id));
    } catch (requestError) {
      setError(requestError instanceof ApiError ? requestError.message : "Unable to archive habit.");
    }
  };

  return (
    <div className="mx-auto h-full max-w-5xl overflow-y-auto px-6 py-10">
      {/* Header */}
      <div className="mb-10 flex flex-col justify-between gap-6 md:flex-row md:items-end">
        <div>
          <span className="mb-2 block text-[10px] font-bold uppercase tracking-widest text-primary">
            Consistency Engine
          </span>
          <h1 className="text-4xl font-bold tracking-tight text-on-surface">Daily Habits</h1>
          <p className="mt-2 max-w-lg text-sm leading-relaxed text-on-surface-variant">
            Track your daily routines, maintain streaks, and build the foundation for long-term productivity.
          </p>
        </div>

        {/* Progress Summary Ring */}
        <div className="flex items-center gap-5 rounded-3xl border border-outline-variant/30 bg-surface-container-low px-6 py-4 shadow-sm">
          <div className="relative flex h-16 w-16 items-center justify-center">
            <svg className="h-full w-full -rotate-90 transform" viewBox="0 0 36 36">
              <circle cx="18" cy="18" r="16" fill="none" className="stroke-surface-container-high" strokeWidth="3" />
              <circle
                cx="18"
                cy="18"
                r="16"
                fill="none"
                className="stroke-primary transition-all duration-1000 ease-out"
                strokeWidth="3"
                strokeDasharray="100"
                strokeDashoffset={100 - progressPercent}
              />
            </svg>
            <div className="absolute text-sm font-bold text-on-surface">{progressPercent}%</div>
          </div>
          <div>
            <h3 className="text-base font-bold text-on-surface">Today's Progress</h3>
            <p className="text-xs font-medium text-on-surface-variant">
              {loading ? "Loading…" : `${completedToday} of ${habits.length} habits completed`}
            </p>
          </div>
        </div>
      </div>

      {/* Error Banner */}
      {error && (
        <div className="mb-6 flex items-center justify-between rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm font-medium text-red-700">
          <div className="flex items-center gap-3">
            <span className="material-symbols-outlined text-red-500" style={{ fontSize: "18px" }}>
              error
            </span>
            {error}
          </div>
          <button className="text-xs font-bold underline" onClick={() => setError(null)} type="button">
            Dismiss
          </button>
        </div>
      )}

      {/* Loading Skeleton */}
      {loading && (
        <div className="mb-10 grid grid-cols-1 gap-6 md:grid-cols-2 lg:grid-cols-3">
          {[1, 2, 3].map((i) => (
            <div
              key={i}
              className="h-44 animate-pulse rounded-2xl border border-outline-variant/20 bg-surface-container-low"
            />
          ))}
        </div>
      )}

      {/* Habit Cards Grid */}
      {!loading && (
        <div className="mb-10 grid grid-cols-1 gap-6 md:grid-cols-2 lg:grid-cols-3">
          {habits.map((habit) => {
            const color = LIFE_AREA_COLORS[habit.lifeArea] || LIFE_AREA_COLORS.personal;
            return (
              <HabitCard
                key={habit.id}
                habit={habit}
                checkInsSet={checkInsSet}
                days={days}
                onToggle={toggle}
                onArchive={archiveHabit}
                color={color}
              />
            );
          })}

          {/* Add New Habit CTA */}
          <button
            onClick={() => setShowModal(true)}
            className="group flex min-h-[170px] flex-col items-center justify-center rounded-2xl border-2 border-dashed border-outline-variant/50 bg-surface-container-lowest/50 p-5 transition-all duration-300 hover:border-primary/40 hover:bg-primary/5 hover:text-primary"
            type="button"
          >
            <div className="mb-3 flex h-12 w-12 items-center justify-center rounded-full bg-surface-container transition-transform group-hover:scale-110 group-hover:bg-primary/10">
              <span className="material-symbols-outlined text-on-surface-variant group-hover:text-primary">
                add
              </span>
            </div>
            <span className="text-sm font-bold text-on-surface-variant group-hover:text-primary">
              Add New Habit
            </span>
          </button>
        </div>
      )}

      {/* Empty State */}
      {!loading && habits.length === 0 && (
        <div className="flex flex-col items-center justify-center py-12 text-center">
          <span
            className="material-symbols-outlined mb-4 text-on-surface-variant/30"
            style={{ fontSize: "64px", fontVariationSettings: "'FILL' 1" }}
          >
            rebase_edit
          </span>
          <p className="font-semibold text-on-surface-variant">No habits yet</p>
          <p className="mt-1 text-sm text-on-surface-variant/60">
            Click "Add New Habit" above to start building your routine.
          </p>
        </div>
      )}

      {/* 7-Day Consistency Matrix */}
      {!loading && habits.length > 0 && (
        <div className="rounded-3xl border border-outline-variant/30 bg-surface-container-lowest p-8 shadow-sm">
          <h3 className="mb-1 text-lg font-bold text-on-surface">7-Day Consistency Matrix</h3>
          <p className="mb-6 text-xs text-on-surface-variant/60">
            Click any cell to toggle completion for that day.
          </p>

          <div className="overflow-x-auto pb-4">
            <div className="min-w-[600px]">
              {/* Column Headers */}
              <div className="mb-4 grid grid-cols-8 gap-4">
                <div className="col-span-1" />
                {days.map((date, idx) => {
                  const isToday = idx === 6;
                  return (
                    <div key={idx} className="flex flex-col items-center gap-0.5">
                      <span
                        className={`text-xs font-bold uppercase tracking-wider ${
                          isToday ? "text-primary" : "text-on-surface-variant"
                        }`}
                      >
                        {DAY_ABBR[date.getDay()]}
                      </span>
                      <span
                        className={`text-[10px] font-semibold tabular-nums ${
                          isToday ? "text-primary/70" : "text-on-surface-variant/50"
                        }`}
                      >
                        {date.getDate()}/{date.getMonth() + 1}
                      </span>
                      {isToday && <span className="mt-0.5 h-1.5 w-1.5 rounded-full bg-primary" />}
                    </div>
                  );
                })}
              </div>

              {/* Habit Rows */}
              <div className="space-y-3">
                {habits.map((habit) => {
                  const color = LIFE_AREA_COLORS[habit.lifeArea] || LIFE_AREA_COLORS.personal;
                  return (
                    <div key={habit.id} className="grid grid-cols-8 items-center gap-4">
                      <div
                        className="col-span-1 truncate pr-4 text-sm font-semibold text-on-surface"
                        title={habit.title}
                      >
                        {habit.title}
                      </div>
                      {days.map((date, idx) => {
                        const key = `${habit.id}:${localDateKey(date)}`;
                        const completed = checkInsSet.has(key);
                        const isToday = idx === 6;
                        return (
                          <div key={idx} className="flex justify-center">
                            <button
                              onClick={() => toggle(habit, date)}
                              aria-label={`${completed ? "Unmark" : "Mark"} ${habit.title} complete for ${DAY_ABBR[date.getDay()]} ${date.getDate()}/${date.getMonth() + 1}`}
                              aria-pressed={completed}
                              title={`${DAY_ABBR[date.getDay()]} ${date.getDate()}/${date.getMonth() + 1} — ${completed ? "Completed ✓" : "Not done"}`}
                              className={`flex h-10 w-10 items-center justify-center rounded-xl transition-all duration-200
                                hover:scale-110 active:scale-95 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/50
                                ${completed ? "shadow-md" : "bg-surface-container-high opacity-40 hover:opacity-70"}`}
                              style={{
                                backgroundColor: completed ? color : undefined,
                                outline: isToday ? `2px solid ${color}60` : undefined,
                                outlineOffset: isToday ? "2px" : undefined,
                              }}
                              type="button"
                            >
                              {completed && (
                                <span
                                  className="material-symbols-outlined text-sm text-white"
                                  style={{ fontVariationSettings: "'FILL' 1, 'wght' 700", fontSize: "16px" }}
                                >
                                  check
                                </span>
                              )}
                            </button>
                          </div>
                        );
                      })}
                    </div>
                  );
                })}
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Add Habit Modal */}
      {showModal && (
        <AddHabitModal
          onClose={() => !saving && setShowModal(false)}
          onAdd={createHabit}
          loading={saving}
        />
      )}
    </div>
  );
}