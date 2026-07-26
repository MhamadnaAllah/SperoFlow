"use client";

import { useState } from "react";

const FREQUENCY_OPTS = [
  { value: "daily",  label: "Daily",  icon: "repeat",         color: "#4f46e5" },
  { value: "weekly", label: "Weekly", icon: "calendar_month", color: "#059669" },
];

export default function AddHabitModal({ onClose, onAdd, loading }) {
  const [name, setName]               = useState("");
  const [frequency, setFrequency]     = useState("daily");
  const [description, setDescription] = useState("");
  const [nameError, setNameError]     = useState("");

  const handleSubmit = async (e) => {
    e.preventDefault();
    if (!name.trim()) {
      setNameError("Habit name is required.");
      return;
    }
    setNameError("");
    await onAdd({ name: name.trim(), frequency, description: description.trim() });
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/40 backdrop-blur-sm"
      onClick={(e) => e.target === e.currentTarget && onClose()}
    >
      <div className="w-full max-w-md bg-surface-container-lowest rounded-3xl border border-outline-variant/30 shadow-2xl overflow-hidden">

        {/* Header */}
        <div className="flex items-center justify-between px-6 py-5 border-b border-outline-variant/20">
          <div className="flex items-center gap-2.5">
            <span
              className="material-symbols-outlined text-primary"
              style={{ fontSize: "22px", fontVariationSettings: "'FILL' 1" }}
            >
              add_task
            </span>
            <h2 className="text-lg font-bold text-on-surface">New Habit</h2>
          </div>
          <button
            onClick={onClose}
            className="w-8 h-8 rounded-full flex items-center justify-center hover:bg-surface-container transition-colors"
            aria-label="Close"
          >
            <span className="material-symbols-outlined text-on-surface-variant" style={{ fontSize: "20px" }}>
              close
            </span>
          </button>
        </div>

        {/* Form */}
        <form onSubmit={handleSubmit} className="px-6 py-6 space-y-5">

          {/* Habit name */}
          <div className="space-y-1.5">
            <label className="text-sm font-semibold text-on-surface-variant">
              Habit Name <span className="text-red-400">*</span>
            </label>
            <input
              type="text"
              value={name}
              onChange={(e) => { setName(e.target.value); setNameError(""); }}
              placeholder="e.g. Morning Run, Read 20 pages…"
              className="w-full px-4 py-3 bg-surface-container rounded-xl border border-outline-variant/30 focus:outline-none focus:ring-2 focus:ring-primary/30 focus:border-primary/50 text-on-surface placeholder-on-surface-variant/40 transition-all"
              autoFocus
            />
            {nameError && (
              <p className="text-xs text-red-400 font-medium">{nameError}</p>
            )}
          </div>

          {/* Frequency */}
          <div className="space-y-1.5">
            <label className="text-sm font-semibold text-on-surface-variant">Frequency</label>
            <div className="grid grid-cols-3 gap-2">
              {FREQUENCY_OPTS.map((opt) => {
                const active = frequency === opt.value;
                return (
                  <button
                    key={opt.value}
                    type="button"
                    onClick={() => setFrequency(opt.value)}
                    className={`flex flex-col items-center gap-1.5 py-3 px-2 rounded-xl border-2 transition-all ${
                      active
                        ? "border-primary bg-primary/10 text-primary"
                        : "border-outline-variant/30 hover:border-primary/30 text-on-surface-variant"
                    }`}
                  >
                    <span
                      className="material-symbols-outlined"
                      style={{
                        fontSize: "20px",
                        fontVariationSettings: "'FILL' 1",
                        color: active ? opt.color : undefined,
                      }}
                    >
                      {opt.icon}
                    </span>
                    <span className="text-xs font-semibold">{opt.label}</span>
                  </button>
                );
              })}
            </div>
          </div>

          {/* Goal / Description */}
          <div className="space-y-1.5">
            <label className="text-sm font-semibold text-on-surface-variant">
              Goal / Description{" "}
              <span className="font-normal opacity-60">(optional)</span>
            </label>
            <textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="What's your goal with this habit?"
              rows={2}
              className="w-full px-4 py-3 bg-surface-container rounded-xl border border-outline-variant/30 focus:outline-none focus:ring-2 focus:ring-primary/30 focus:border-primary/50 text-on-surface placeholder-on-surface-variant/40 transition-all resize-none"
            />
          </div>

          {/* Actions */}
          <div className="flex gap-3 pt-1">
            <button
              type="button"
              onClick={onClose}
              disabled={loading}
              className="flex-1 py-3 rounded-xl border border-outline-variant/40 text-on-surface-variant font-semibold hover:bg-surface-container transition-colors disabled:opacity-40"
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={loading}
              className="flex-1 py-3 rounded-xl bg-primary text-on-primary font-bold hover:opacity-90 active:scale-95 transition-all disabled:opacity-50 disabled:cursor-not-allowed flex items-center justify-center gap-2"
            >
              {loading ? (
                <>
                  <span className="w-4 h-4 border-2 border-on-primary/30 border-t-on-primary rounded-full animate-spin" />
                  Saving…
                </>
              ) : (
                <>
                  <span className="material-symbols-outlined" style={{ fontSize: "18px" }}>
                    add
                  </span>
                  Add Habit
                </>
              )}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
