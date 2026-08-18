"use client";

import { useState } from "react";
import { goalsApi, tasksApi } from "@/lib/api/client";

function parseResourceLink(raw) {
  if (!raw) return { url: "#", label: "Documentation", type: "Docs" };
  const text = String(raw).trim();

  // Pattern 1: [@type@Title](url)
  const atMatch = text.match(/\[@([a-zA-Z0-9_\-]+)@([^\]]+)\]\((https?:\/\/[^\)\s]+)\)/);
  if (atMatch) {
    return {
      type: atMatch[1].charAt(0).toUpperCase() + atMatch[1].slice(1).toLowerCase(),
      label: atMatch[2].trim(),
      url: atMatch[3].trim(),
    };
  }

  // Pattern 2: [Title](url)
  const mdMatch = text.match(/\[([^\]]+)\]\((https?:\/\/[^\)\s]+)\)/);
  if (mdMatch) {
    return {
      type: "Article",
      label: mdMatch[1].trim(),
      url: mdMatch[2].trim(),
    };
  }

  // Pattern 3: [Type] Title - url or [Type] url
  const bracketMatch = text.match(/^\[([a-zA-Z0-9_\-\s]+)\]\s*(.*?)(?:[\s\-–—:]+)(https?:\/\/[^\s]+)$/);
  if (bracketMatch) {
    return {
      type: bracketMatch[1].trim(),
      label: bracketMatch[2].trim() || bracketMatch[1].trim(),
      url: bracketMatch[3].trim(),
    };
  }

  // Pattern 4: url found with label
  const urlMatch = text.match(/(https?:\/\/[^\s]+)/);
  if (urlMatch) {
    const url = urlMatch[1];
    let label = text.replace(url, "").replace(/^[\s\-–—:\[\]\(\)]+/, "").replace(/[\s\-–—:\[\]\(\)]+$/, "").trim() || url;
    let type = "Docs";
    if (/youtube\.com|youtu\.be/i.test(url) || /video/i.test(label) || /video/i.test(text)) type = "Video";
    else if (/course|udemy|coursera|edx|educative/i.test(url) || /course/i.test(label) || /course/i.test(text)) type = "Course";
    else if (/feed|blog|medium|dev\.to|daily\.dev/i.test(url) || /feed/i.test(label)) type = "Feed";
    else if (/article|tutorial|guide/i.test(label) || /article/i.test(text)) type = "Article";
    else if (/github\.com/i.test(url)) type = "Repo";
    
    return { url, label, type };
  }

  return { url: "#", label: text, type: "Resource" };
}

function getResourceTypeBadge(type) {
  const t = String(type || "Docs").toLowerCase();
  if (t.includes("article") || t.includes("guide") || t.includes("tutorial")) {
    return {
      bg: "bg-amber-100 text-amber-900 border-amber-300",
      icon: "menu_book",
      label: "Article",
    };
  }
  if (t.includes("video") || t.includes("youtube")) {
    return {
      bg: "bg-purple-100 text-purple-900 border-purple-300",
      icon: "smart_display",
      label: "Video",
    };
  }
  if (t.includes("course")) {
    return {
      bg: "bg-emerald-100 text-emerald-900 border-emerald-300",
      icon: "school",
      label: "Course",
    };
  }
  if (t.includes("feed") || t.includes("blog") || t.includes("post")) {
    return {
      bg: "bg-pink-100 text-pink-900 border-pink-300",
      icon: "rss_feed",
      label: "Feed",
    };
  }
  if (t.includes("repo") || t.includes("github")) {
    return {
      bg: "bg-slate-100 text-slate-900 border-slate-300",
      icon: "code",
      label: "Repo",
    };
  }
  return {
    bg: "bg-blue-100 text-blue-900 border-blue-300",
    icon: "description",
    label: "Docs",
  };
}

export default function NodeDetailModal({ milestone, goal, onClose, onStateChange, onTaskAdded }) {
  const [addingTask, setAddingTask] = useState(false);
  const [taskTitle, setTaskTitle] = useState("");
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState(null);

  // Extract work items/lines from description
  const rawDesc = milestone?.description || "";
  
  // Extract overview paragraph (text before Key Work Items or Resources)
  const overview = rawDesc
    .split(/Key Work Items:|Resources:|Subtasks:/i)[0]
    .trim();

  // Extract work items
  let taskLines = [];
  if (rawDesc.includes("Key Work Items:")) {
    const workPart = rawDesc.split(/Key Work Items:/i)[1].split(/Resources:/i)[0];
    taskLines = workPart
      .split("\n")
      .map((l) => l.trim().replace(/^[-•*]\s*/, ""))
      .filter((l) => l.length > 0);
  } else {
    taskLines = overview
      .split("\n")
      .map((l) => l.trim().replace(/^[-•*]\s*/, ""))
      .filter((l) => l.length > 0 && (l.startsWith("•") || l.startsWith("-") || l.length < 150));
  }

  if (taskLines.length === 0) {
    taskLines = [
      `Review core fundamentals for ${milestone.title}`,
      `Complete hands-on implementation project for ${milestone.title}`,
      `Verify test cases and code review for ${milestone.title}`,
    ];
  }

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
            {overview || `Master key principles and practical implementations for ${milestone.title}.`}
          </div>
        </div>

        {/* Direct Clickable Resource Hyperlinks with Type Badges */}
        <div className="mt-6">
          <h3 className="text-xs font-bold uppercase tracking-wider text-slate-500">Curated Learning Resources & Documentation</h3>
          <div className="mt-3 flex flex-col gap-2">
            {resources.length > 0 ? (
              resources.map((res, rIdx) => {
                const link = parseResourceLink(res);
                const badge = getResourceTypeBadge(link.type);
                const hasUrl = link.url && link.url !== "#";
                return (
                  <a
                    key={rIdx}
                    href={hasUrl ? link.url : undefined}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="flex items-center justify-between gap-3 rounded-xl border border-slate-200 bg-white p-3 hover:border-blue-400 hover:bg-blue-50/30 transition shadow-2xs group"
                  >
                    <div className="flex items-center gap-3 min-w-0">
                      <span className={`inline-flex items-center gap-1 rounded-md px-2 py-0.5 text-[11px] font-bold uppercase tracking-wider border shrink-0 ${badge.bg}`}>
                        <span className="material-symbols-outlined text-[13px]">{badge.icon}</span>
                        <span>{badge.label}</span>
                      </span>
                      <span className="text-sm font-semibold text-slate-800 group-hover:text-blue-700 truncate">
                        {link.label}
                      </span>
                    </div>
                    {hasUrl && (
                      <span className="material-symbols-outlined text-[18px] text-slate-400 group-hover:text-blue-600 shrink-0">
                        open_in_new
                      </span>
                    )}
                  </a>
                );
              })
            ) : (
              <div className="rounded-xl border border-slate-100 bg-slate-50/50 p-3 text-xs text-slate-500 italic">
                No external resource links attached for this node.
              </div>
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
