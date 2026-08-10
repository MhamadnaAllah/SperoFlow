"use client";

import { useState } from "react";
import NodeDetailModal from "./NodeDetailModal";

export default function GoalPathRoadmap({ milestones = [], goal = null, busy = false, onStateChange = null, onTaskAdded = null, proposalMode = false, onApprove = null, onCancel = null }) {
  const [selectedNode, setSelectedNode] = useState(null);

  if (!milestones || milestones.length === 0) {
    return null;
  }

  const completedCount = milestones.filter((m) => m.state === "completed").length;
  const overallProgress = proposalMode ? 0 : (milestones.length > 0 ? Math.round((completedCount / milestones.length) * 100) : 0);

  return (
    <div className="relative w-full py-8">
      {/* Central Spine Line (Mobile: left-6, Desktop: center) */}
      <div className="absolute left-6 md:left-1/2 top-0 bottom-0 w-1 bg-slate-200 -translate-x-1/2 z-0 rounded-full">
        {/* Dynamic Progress Line */}
        <div
          className="w-full bg-blue-600 rounded-full absolute top-0 left-0 transition-all duration-500"
          style={{ height: `${overallProgress}%` }}
        />
      </div>

      {/* Nodes Timeline List */}
      <div className="space-y-8 relative z-10 flex flex-col">
        {milestones.map((node, index) => {
          const isCompleted = node.state === "completed";
          const isFirstIncomplete = !isCompleted && milestones.slice(0, index).every((m) => m.state === "completed");
          const isInProgress = !proposalMode && isFirstIncomplete && completedCount > 0;
          const isEven = index % 2 === 0;

          // Status Badge details
          let badgeText = proposalMode ? `Proposed Module ${index + 1}` : "Upcoming Node";
          let badgeClass = "bg-slate-100 text-slate-600 border-slate-200";
          let borderClass = "border-slate-200 shadow-sm hover:border-blue-400";
          let iconName = "circle";
          let iconColor = "text-slate-400";

          if (isCompleted) {
            badgeText = "Completed Module";
            badgeClass = "bg-emerald-50 text-emerald-700 border-emerald-200";
            borderClass = "border-emerald-500 glow-green shadow-md";
            iconName = "check_circle";
            iconColor = "text-emerald-600";
          } else if (isInProgress) {
            badgeText = "Current Focus";
            badgeClass = "bg-blue-50 text-blue-700 border-blue-200 animate-pulse";
            borderClass = "border-blue-600 glow-blue shadow-lg ring-2 ring-blue-100";
            iconName = "play_circle";
            iconColor = "text-blue-600";
          }

          return (
            <div
              key={node.id || node.sortOrder || index}
              className={`milestone-node flex w-full relative group ${
                isEven ? "justify-start md:justify-end pl-16 md:pl-0" : "justify-start pl-16 md:pl-0"
              }`}
            >
              <div
                onClick={() => setSelectedNode(node)}
                className={`w-full md:w-[calc(50%-2rem)] cursor-pointer bg-white border rounded-2xl p-5 transition-all duration-300 hover:-translate-y-1 relative ${borderClass}`}
              >
                {/* Desktop Connecting Dot on Central Spine */}
                <div
                  className={`absolute top-1/2 -translate-y-1/2 w-5 h-5 rounded-full ring-4 ring-white z-20 hidden md:block ${
                    isEven ? "left-[-2.75rem]" : "right-[-2.75rem]"
                  } ${
                    isCompleted ? "bg-emerald-500" : isInProgress ? "bg-blue-600 animate-pulse" : "bg-slate-300"
                  }`}
                />
                
                {/* Mobile Connecting Dot */}
                <div
                  className={`absolute left-[-2.75rem] top-1/2 -translate-y-1/2 w-4 h-4 rounded-full ring-4 ring-slate-100 z-20 md:hidden ${
                    isCompleted ? "bg-emerald-500" : isInProgress ? "bg-blue-600 animate-pulse" : "bg-slate-300"
                  }`}
                />

                {/* Card Header */}
                <div className="flex justify-between items-start gap-2 mb-3">
                  <span className={`text-[10px] font-bold uppercase tracking-wider px-2.5 py-1 rounded-md border ${badgeClass}`}>
                    Step {index + 1}: {badgeText}
                  </span>
                  <div className="flex items-center gap-1">
                    {node.estimatedHours !== null && node.estimatedHours !== undefined && (
                      <span className="rounded-md bg-slate-100 px-2 py-0.5 text-xs font-bold text-slate-700">
                        {node.estimatedHours}h
                      </span>
                    )}
                    <span className={`material-symbols-outlined ${iconColor}`} style={{ fontVariationSettings: "'FILL' 1" }}>
                      {iconName}
                    </span>
                  </div>
                </div>

                {/* Node Title & Icon */}
                <div className="flex items-center gap-3 mb-2">
                  <div className={`p-2 rounded-xl ${isCompleted ? "bg-emerald-50 text-emerald-600" : "bg-blue-50 text-blue-600"}`}>
                    <span className="material-symbols-outlined text-[20px]">account_tree</span>
                  </div>
                  <h3 className={`text-base font-bold transition ${isCompleted ? "text-slate-700 line-through" : "text-slate-900"}`}>
                    {node.title}
                  </h3>
                </div>

                {/* Node Description Snippet */}
                {node.description && (
                  <p className="text-xs leading-relaxed text-slate-600 line-clamp-2 mt-1">
                    {node.description}
                  </p>
                )}

                {/* Progress bar for in-progress node */}
                {isInProgress && (
                  <div className="mt-3">
                    <div className="w-full bg-slate-100 rounded-full h-1.5 overflow-hidden">
                      <div className="bg-blue-600 h-1.5 rounded-full transition-all duration-500" style={{ width: "45%" }} />
                    </div>
                    <div className="text-[10px] font-semibold text-blue-600 text-right mt-1">Active Roadmap Focus • Click to Explore Node Page</div>
                  </div>
                )}

                {/* Resources Badges */}
                {node.resources?.length > 0 && (
                  <div className="mt-3 flex flex-wrap items-center gap-1.5" onClick={(e) => e.stopPropagation()}>
                    {node.resources.slice(0, 2).map((res, rIdx) => {
                      const urlMatch = String(res).match(/(https?:\/\/[^\s]+)/);
                      const url = urlMatch ? urlMatch[1] : `https://www.google.com/search?q=${encodeURIComponent(res)}`;
                      const label = urlMatch ? String(res).replace(urlMatch[1], "").replace(/^[\s\-–—:]+/, "").trim() || urlMatch[1] : res;
                      return (
                        <a
                          key={rIdx}
                          href={url}
                          target="_blank"
                          rel="noopener noreferrer"
                          className="inline-flex items-center gap-1 rounded-md bg-blue-50/80 border border-blue-200 px-2 py-0.5 text-[11px] font-medium text-blue-700 hover:bg-blue-600 hover:text-white transition"
                        >
                          <span className="material-symbols-outlined" style={{ fontSize: "12px" }}>open_in_new</span>
                          <span className="truncate max-w-[140px]">{label}</span>
                        </a>
                      );
                    })}
                    {node.resources.length > 2 && (
                      <span className="text-[10px] font-bold text-slate-400">+{node.resources.length - 2} more</span>
                    )}
                  </div>
                )}

                {/* Hover / Click Tooltip Popup Card */}
                <div className="tooltip-card absolute left-0 md:group-hover:block hidden top-full mt-2 w-full bg-white/95 backdrop-blur-md border border-slate-200 rounded-2xl p-4 shadow-xl z-30 pointer-events-none">
                  <h4 className="text-xs font-bold text-slate-900 border-b border-slate-100 pb-2 mb-2 flex items-center justify-between">
                    <span>Node Overview & Key Objectives</span>
                    <span className="text-[10px] text-blue-600 uppercase font-semibold">GraphRAG Powered</span>
                  </h4>
                  <p className="text-xs text-slate-600 leading-relaxed">
                    {node.description || "Master core concepts and complete practical projects."}
                  </p>
                </div>
              </div>
            </div>
          );
        })}
      </div>

      {/* Interactive Node Detail Modal */}
      {selectedNode && (
        <NodeDetailModal
          milestone={selectedNode}
          goal={goal}
          onClose={() => setSelectedNode(null)}
          onStateChange={onStateChange}
          onTaskAdded={onTaskAdded}
        />
      )}
    </div>
  );
}
