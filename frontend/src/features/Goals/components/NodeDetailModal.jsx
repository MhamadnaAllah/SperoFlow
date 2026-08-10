"use client";

import { useState } from "react";
import { goalsApi, tasksApi } from "@/lib/api/client";

function parseResourceLink(raw) {
  if (!raw) return { url: "#", label: "" };
  const text = String(raw).trim();
  const urlMatch = text.match(/(https?:\/\/[^\s]+)/);
  if (urlMatch) {
    const url = urlMatch[1];
    const label = text.replace(url, "").replace(/^[\s\-–—:]+/, "").trim() || url;
    return { url, label };
  }
  const searchUrl = `https://www.google.com/search?q=${encodeURIComponent(text)}`;
  return { url: searchUrl, label: text };
}

export default function NodeDetailModal({ milestone, goal, onClose, onStateChange, onTaskAdded }) {
  const [addingTask, setAddingTask] = useState(false);
  const [taskTitle, setTaskTitle] = useState("");
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState(null);

  // Extract work items/lines from description
  const rawDesc = milestone?.description || "";
  // Split description into clean lines excluding "Resources:" section if appended
  const cleanDesc = rawDesc.split(/Resources:/i)[0].trim();
  const taskLines = cleanDesc
    .split("\n")
    .map((l) => l.trim().replace(/^[-•*]\s*/, ""))
    .filter((l) => l.length > 0);

  const initialChecked = {};
  taskLines.forEach((_, idx) => {
    initialChecked[idx] = milestone?.state === "completed";
  });
  const [checkedState, setCheckedState] = useState(initialChecked);

  if (!milestone) return null;

  const isCompleted = milestone.state === "completed";
  const estimatedHours = milestone.estimatedHours ?? 4;

  // Extract resources list
  let resources = milestone.resources || [];
  if (resources.length === 0 && rawDesc.includes("Resources:")) {
    const resPart = rawDesc.split(/Resources:/i)[1];
    if (resPart) {
      resources = resPart
        .split("\n")
        .map((r) => r.replace(/^[-•*]\s*/, "").trim())
        .filter((r) => r.length > 0);
    }
  }

  const toggleTaskCheck = async (index) => {
    const updated = { ...checkedState, [index]: !checkedState[index] };
    setCheckedState(updated);

    const total = taskLines.length;
    const completedCount = Object.values(updated).filter(Boolean).length;
    
    if (total > 0 && completedCount === total && !isCompleted && goal && onStateChange) {
      setBusy(true);
      try {
        await onStateChange(milestone, "completed");
        setNotice("All work items completed! Node marked as Complete 🎉");
      } catch (e) {
        setNotice("Could not update milestone status.");
      } finally {
        setBusy(false);
      }
    }
  };

  const handleCreateTask = async (titleToCreate) => {
    if (!titleToCreate.trim() || !goal) return;
    setBusy(true);
    try {
      const newTask = await tasksApi.create({
        title: titleToCreate.trim(),
        description: `Linked milestone node: ${milestone.title}`,
        goalId: goal.id,
        quadrant: "q2",
      });
      setNotice("Task added to your Eisenhower Matrix & Calendar!");
      if (onTaskAdded) onTaskAdded(newTask);
      setTaskTitle("");
      setAddingTask(false);
    } catch (err) {
      setNotice("Failed to add task.");
    } finally {
      setBusy(false);
    }
  };

  const totalTasks = taskLines.length || 1;
  const doneTasks = Object.values(checkedState).filter(Boolean).length;
  const progressPct = isCompleted ? 100 : Math.round((doneTasks / totalTasks) * 100);

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/60 p-4 backdrop-blur-sm animate-fade-in">
      <div className="relative w-full max-w-3xl max-h-[90vh] overflow-y-auto rounded-2xl bg-white p-6 sm:p-8 shadow-2xl border border-slate-200">
        
        {/* Header Bar */}
        <div className="flex items-start justify-between gap-4 border-b border-slate-100 pb-5">
          <div>
            <div className="flex flex-wrap items-center gap-2">
              <span className="rounded-md bg-blue-50 px-2.5 py-1 text-xs font-bold uppercase tracking-wider text-blue-700">
                {goal?.title ? goal.title : "GraphRAG Goal Node"}
              </span>
              <span className={`rounded-md px-2.5 py-1 text-xs font-bold uppercase tracking-wider ${
                isCompleted ? "bg-emerald-100 text-emerald-700" : "bg-amber-100 text-amber-800"
              }`}>
                {isCompleted ? "Completed Module" : "Active Focus Node"}
              </span>
            </div>
            <h2 className="mt-3 text-2xl font-black text-slate-900 flex items-center gap-3">
              <span className="material-symbols-outlined text-primary font-bold">account_tree</span>
              {milestone.title}
            </h2>
          </div>
          
          <button
            onClick={onClose}
            className="rounded-full p-2 text-slate-400 hover:bg-slate-100 hover:text-slate-700 transition"
            aria-label="Close detail modal"
          >
            <span className="material-symbols-outlined">close</span>
          </button>
        </div>

        {/* Notice */}
        {notice && (
          <div className="mt-4 rounded-xl bg-blue-50 border border-blue-200 p-3 text-xs font-semibold text-blue-800 flex justify-between items-center">
            <span>{notice}</span>
            <button onClick={() => setNotice(null)} className="text-blue-600 hover:underline font-bold">Dismiss</button>
          </div>
        )}

        {/* Grid Stats */}
        <div className="mt-6 grid grid-cols-2 sm:grid-cols-3 gap-3">
          <div className="rounded-xl border border-slate-100 bg-slate-50/60 p-4 text-center">
            <span className="text-xs font-bold text-slate-500 uppercase tracking-wider">Estimated Effort</span>
            <p className="mt-1 text-lg font-black text-slate-900">{estimatedHours} Hours</p>
          </div>
          <div className="rounded-xl border border-slate-100 bg-slate-50/60 p-4 text-center">
            <span className="text-xs font-bold text-slate-500 uppercase tracking-wider">Node Progress</span>
            <p className={`mt-1 text-lg font-black ${isCompleted ? "text-emerald-600" : "text-blue-600"}`}>
              {progressPct}% Done
            </p>
          </div>
          <div className="col-span-2 sm:col-span-1 rounded-xl border border-slate-100 bg-slate-50/60 p-4 text-center">
            <span className="text-xs font-bold text-slate-500 uppercase tracking-wider">RAG Grounding</span>
            <p className="mt-1 text-xs font-bold text-slate-700">google.gemma-4-31b</p>
          </div>
        </div>

        {/* Node Overview */}
        <div className="mt-6">
          <h3 className="text-xs font-bold uppercase tracking-wider text-slate-500">Node Overview & Objectives</h3>
          <div className="mt-2 rounded-xl bg-slate-50/70 p-4 text-sm leading-relaxed text-slate-700 border border-slate-100">
            {cleanDesc || `Master key principles and practical implementations for ${milestone.title}.`}
          </div>
        </div>

        {/* Clickable Resource Hyperlinks */}
        <div className="mt-6">
          <h3 className="text-xs font-bold uppercase tracking-wider text-slate-500">Clickable Learning Resources & Documentation</h3>
          <div className="mt-3 flex flex-wrap gap-2">
            {resources.length > 0 ? (
              resources.map((res, rIdx) => {
                const link = parseResourceLink(res);
                return (
                  <a
                    key={rIdx}
                    href={link.url}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="inline-flex items-center gap-2 rounded-xl border border-blue-200 bg-blue-50/50 px-3.5 py-2 text-xs font-semibold text-blue-800 hover:bg-blue-600 hover:text-white transition shadow-2xs group"
                  >
                    <span className="material-symbols-outlined text-[16px] text-blue-600 group-hover:text-white">open_in_new</span>
                    <span>{link.label}</span>
                  </a>
                );
              })
            ) : (
              <a
                href={`https://www.google.com/search?q=${encodeURIComponent(milestone.title + " documentation tutorial")}`}
                target="_blank"
                rel="noopener noreferrer"
                className="inline-flex items-center gap-2 rounded-xl border border-blue-200 bg-blue-50/50 px-3.5 py-2 text-xs font-semibold text-blue-800 hover:bg-blue-600 hover:text-white transition"
              >
                <span className="material-symbols-outlined text-[16px]">open_in_new</span>
                <span>Search Documentation for {milestone.title}</span>
              </a>
            )}
          </div>
        </div>

        {/* Work Items Checklist with Interactive Checkboxes */}
        <div className="mt-6">
          <div className="flex items-center justify-between">
            <h3 className="text-xs font-bold uppercase tracking-wider text-slate-500">Interactive Tasks & Progress Items</h3>
            {goal && (
              <button
                onClick={() => setAddingTask((prev) => !prev)}
                className="text-xs font-bold text-primary hover:underline flex items-center gap-1"
              >
                <span className="material-symbols-outlined text-[16px]">add</span>
                Add Work Item
              </button>
            )}
          </div>

          {addingTask && (
            <div className="mt-3 flex items-center gap-2">
              <input
                type="text"
                value={taskTitle}
                onChange={(e) => setTaskTitle(e.target.value)}
                placeholder="Enter work item title..."
                className="flex-1 rounded-xl border border-slate-300 px-3.5 py-2 text-sm text-slate-900 focus:border-blue-600 focus:outline-none"
              />
              <button
                disabled={busy || !taskTitle.trim()}
                onClick={() => handleCreateTask(taskTitle)}
                className="rounded-xl bg-primary px-4 py-2 text-xs font-bold text-white hover:bg-blue-700 disabled:opacity-50"
              >
                Save
              </button>
            </div>
          )}

          <div className="mt-3 space-y-2">
            {taskLines.length > 0 ? (
              taskLines.map((line, lIdx) => {
                const checked = Boolean(checkedState[lIdx]);
                return (
                  <div
                    key={lIdx}
                    className={`flex items-center justify-between rounded-xl border p-3 transition ${
                      checked ? "border-emerald-200 bg-emerald-50/50" : "border-slate-100 bg-slate-50/50 hover:bg-slate-100/60"
                    }`}
                  >
                    <button
                      onClick={() => toggleTaskCheck(lIdx)}
                      className="flex items-center gap-3 text-left flex-1"
                      type="button"
                    >
                      <span className={`material-symbols-outlined text-[20px] ${
                        checked ? "text-emerald-600" : "text-slate-400 hover:text-blue-600"
                      }`}>
                        {checked ? "check_box" : "check_box_outline_blank"}
                      </span>
                      <span className={`text-sm font-medium ${checked ? "line-through text-slate-500" : "text-slate-800"}`}>
                        {line}
                      </span>
                    </button>

                    {goal && (
                      <button
                        onClick={() => handleCreateTask(line)}
                        disabled={busy}
                        className="inline-flex items-center gap-1 rounded-lg border border-slate-200 bg-white px-2.5 py-1 text-xs font-bold text-slate-700 hover:bg-slate-50 shadow-2xs transition"
                        title="Convert to SperoFlow Task"
                      >
                        <span className="material-symbols-outlined text-[14px] text-primary">bolt</span>
                        Add Task
                      </button>
                    )}
                  </div>
                );
              })
            ) : (
              <div className="flex items-center justify-between rounded-xl border border-slate-100 bg-slate-50/50 p-3">
                <span className="text-sm text-slate-700">Complete practical exercise and capstone implementation for {milestone.title}</span>
                {goal && (
                  <button
                    onClick={() => handleCreateTask(`Practice project for ${milestone.title}`)}
                    disabled={busy}
                    className="inline-flex items-center gap-1 rounded-lg border border-slate-200 bg-white px-2.5 py-1 text-xs font-bold text-slate-700 hover:bg-slate-50 shadow-2xs"
                  >
                    <span className="material-symbols-outlined text-[14px] text-primary">bolt</span>
                    Add Task
                  </button>
                )}
              </div>
            )}
          </div>
        </div>

        {/* Modal Actions */}
        <div className="mt-8 flex flex-wrap items-center justify-between gap-3 border-t border-slate-100 pt-5">
          {onStateChange && (
            <button
              onClick={() => onStateChange(milestone, isCompleted ? "pending" : "completed")}
              className={`inline-flex items-center gap-2 rounded-xl px-5 py-2.5 text-sm font-bold shadow-sm transition ${
                isCompleted
                  ? "border border-slate-300 bg-white text-slate-700 hover:bg-slate-50"
                  : "bg-emerald-600 text-white hover:bg-emerald-700"
              }`}
            >
              <span className="material-symbols-outlined text-[18px]">check</span>
              {isCompleted ? "Mark Incomplete" : "Mark Node Complete"}
            </button>
          )}

          <button
            onClick={onClose}
            className="rounded-xl border border-slate-300 bg-white px-5 py-2.5 text-sm font-bold text-slate-700 hover:bg-slate-50"
          >
            Close Node View
          </button>
        </div>

      </div>
    </div>
  );
}
