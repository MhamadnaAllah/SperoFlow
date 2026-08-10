"use client";

import { useState } from "react";

export default function AddHabitModal({ onClose, onAdd, loading }) {
  const [name, setName] = useState("");
  const [frequency, setFrequency] = useState("daily");
  const [description, setDescription] = useState("");

  const handleSubmit = (e) => {
    e.preventDefault();
    if (!name.trim()) return;
    onAdd({ name: name.trim(), frequency, description: description.trim() });
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4"
      style={{ backgroundColor: "rgba(0, 0, 0, 0.4)", backdropFilter: "blur(4px)" }}
      onClick={(e) => e.target === e.currentTarget && onClose()}
    >
      <div className="w-full max-w-md rounded-3xl border border-outline-variant/30 bg-surface-container-lowest p-6 shadow-2xl animate-in fade-in zoom-in-95">
        <div className="mb-6 flex items-center justify-between">
          <h2 className="text-xl font-bold text-on-surface">Add New Habit</h2>
          <button
            onClick={onClose}
            className="flex h-8 w-8 items-center justify-center rounded-full text-on-surface-variant/60 hover:bg-surface-container hover:text-on-surface"
            type="button"
          >
            <span className="material-symbols-outlined text-sm">close</span>
          </button>
        </div>

        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="mb-1.5 block text-xs font-bold uppercase tracking-wider text-on-surface-variant">
              Habit Name *
            </label>
            <input
              type="text"
              required
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="e.g. Morning Meditation, Read 20 pages"
              className="w-full rounded-xl border border-outline-variant/40 bg-surface-container-low px-4 py-3 text-sm text-on-surface outline-none transition-all focus:border-primary focus:bg-white"
            />
          </div>

          <div>
            <label className="mb-1.5 block text-xs font-bold uppercase tracking-wider text-on-surface-variant">
              Frequency
            </label>
            <select
              value={frequency}
              onChange={(e) => setFrequency(e.target.value)}
              className="w-full rounded-xl border border-outline-variant/40 bg-surface-container-low px-4 py-3 text-sm text-on-surface outline-none transition-all focus:border-primary focus:bg-white"
            >
              <option value="daily">Daily</option>
              <option value="weekly">Weekly</option>
            </select>
          </div>

          <div>
            <label className="mb-1.5 block text-xs font-bold uppercase tracking-wider text-on-surface-variant">
              Description (Optional)
            </label>
            <textarea
              rows={3}
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="Why this habit matters or specific targets..."
              className="w-full rounded-xl border border-outline-variant/40 bg-surface-container-low px-4 py-3 text-sm text-on-surface outline-none transition-all focus:border-primary focus:bg-white"
            />
          </div>

          <div className="flex items-center justify-end gap-3 pt-4">
            <button
              type="button"
              onClick={onClose}
              className="rounded-xl px-4 py-2.5 text-sm font-bold text-on-surface-variant hover:bg-surface-container"
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={loading || !name.trim()}
              className="rounded-xl bg-primary px-6 py-2.5 text-sm font-bold text-on-primary shadow-sm transition-all hover:bg-primary/90 disabled:opacity-50"
            >
              {loading ? "Adding..." : "Add Habit"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
