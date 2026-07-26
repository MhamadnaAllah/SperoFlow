"use client";

import { useState } from "react";

export default function RoadmapScreen({ roadmap, onExploreNode, onUpdateModule }) {
  const [activeAccordionId, setActiveAccordionId] = useState(null);

  // Toggle checklist tasks
  const handleToggleTask = (module, taskId) => {
    const updatedTasks = module.tasks.map((task) =>
      task.id === taskId ? { ...task, completed: !task.completed } : task
    );

    const completedCount = updatedTasks.filter((t) => t.completed).length;
    let newProgress = Math.round((completedCount / updatedTasks.length) * 100);

    let newStatus = module.status;
    let newCategory = module.category;

    if (newProgress === 100) {
      newStatus = "completed";
      newCategory = "Core Module";
    } else if (newProgress > 0) {
      newStatus = "progress";
      newCategory = "Current Focus";
    } else {
      newStatus = "progress";
    }

    const updatedModule = {
      ...module,
      tasks: updatedTasks,
      progress: newProgress,
      status: newStatus,
      category: newCategory,
    };

    onUpdateModule(updatedModule);

    // Dynamic cascade unlock logic
    if (newProgress === 100) {
      const moduleIndex = roadmap.modules.findIndex((m) => m.id === module.id);
      if (moduleIndex !== -1 && moduleIndex + 1 < roadmap.modules.length) {
        const nextModule = roadmap.modules[moduleIndex + 1];
        if (nextModule.status === "locked") {
          const unlockedModule = {
            ...nextModule,
            status: "progress",
            category: "Current Focus",
            progress: 0,
          };
          onUpdateModule(unlockedModule);
        }
      }
    }
  };

  const getBadgeStyles = (status) => {
    switch (status) {
      case "completed":
        return "bg-status-completed/10 text-status-completed border border-status-completed/20";
      case "progress":
        return "bg-primary/10 text-primary border border-primary/20";
      default:
        return "bg-outline/10 text-outline border border-outline/20";
    }
  };

  return (
    <div className="space-y-12 animate-fade-in max-w-5xl mx-auto">
      {/* Header section */}
      <div className="text-center space-y-4">
        <h1
          id="roadmap-screen-title"
          className="text-4xl md:text-5xl font-headline font-extrabold text-on-surface tracking-tight leading-none"
        >
          {roadmap.title}
        </h1>
        <p className="text-sm text-on-surface-variant max-w-2xl mx-auto leading-relaxed">
          {roadmap.description}
        </p>
      </div>

      {/* Interactive Legend */}
      <div className="flex justify-center items-center gap-6 bg-surface-container-low border border-outline-variant/40 py-2.5 px-4 rounded-full max-w-md mx-auto text-[11px] font-headline font-bold">
        <div className="flex items-center gap-1.5">
          <span className="w-2.5 h-2.5 rounded-full bg-status-completed"></span>
          <span className="text-on-surface">Core Module (100%)</span>
        </div>
        <div className="flex items-center gap-1.5">
          <span className="w-2.5 h-2.5 rounded-full bg-primary animate-pulse"></span>
          <span className="text-on-surface">Current Focus</span>
        </div>
        <div className="flex items-center gap-1.5">
          <span className="w-2.5 h-2.5 rounded-full bg-outline"></span>
          <span className="text-outline">Locked</span>
        </div>
      </div>

      {/* Roadmap vertical tree layout */}
      <div className="relative py-12">
        {/* Central Spine Connector Line */}
        <div className="absolute left-6 md:left-1/2 top-0 bottom-0 w-1 bg-outline-variant -translate-x-1/2 z-0 rounded-full">
          <div
            className="w-full bg-primary rounded-full absolute top-0 left-0 transition-all duration-1000 ease-in-out"
            style={{
              height: `${
                roadmap.modules.filter((m) => m.status === "completed").length * 20 +
                (roadmap.modules.some((m) => m.status === "progress") ? 10 : 0)
              }%`,
            }}
          />
        </div>

        {/* Nodes Container */}
        <div className="space-y-16 relative z-10 flex flex-col">
          {roadmap.modules.map((module, index) => {
            const isLeft = index % 2 === 1;
            const isCompleted = module.status === "completed";
            const isProgress = module.status === "progress";
            const isLocked = module.status === "locked";

            return (
              <div
                key={module.id}
                className={`milestone-node flex w-full relative ${
                  isLeft ? "justify-start md:justify-start" : "justify-start md:justify-end"
                } pl-12 md:pl-0`}
              >
                {/* Connecting Node Dots */}
                <div
                  className={`absolute left-0 md:left-1/2 top-1/2 -translate-y-1/2 -translate-x-1/2 w-5 h-5 rounded-full border-4 z-20 transition-all duration-300 ${
                    isCompleted
                      ? "bg-status-completed border-surface-container-lowest ring-4 ring-status-completed/10"
                      : isProgress
                      ? "bg-primary border-surface-container-lowest ring-4 ring-primary/20 animate-pulse"
                      : "bg-surface-dim border-outline-variant"
                  }`}
                />

                {/* Main Card Component */}
                <div
                  className={`w-full md:w-[calc(50%-2rem)] bg-surface-container-lowest border rounded-xl p-6 transition-all duration-300 relative group flex flex-col ${
                    isCompleted
                      ? "border-status-completed shadow-md hover:-translate-y-1 glow-green"
                      : isProgress
                      ? "border-primary shadow-md hover:-translate-y-1 glow-blue"
                      : "border-outline-variant/60 opacity-70"
                  }`}
                >
                  {/* Card head metadata */}
                  <div className="flex justify-between items-start mb-3">
                    <span
                      className={`text-[10px] font-mono font-bold tracking-wider uppercase px-2 py-0.5 rounded ${getBadgeStyles(
                        module.status
                      )}`}
                    >
                      {module.category}
                    </span>
                    <span className="text-[11px] font-mono font-bold text-outline">{module.id}</span>
                  </div>

                  {/* Icon & Title */}
                  <div className="flex items-center gap-3.5 mb-3">
                    <div
                      className={`p-2 rounded-lg ${
                        isCompleted
                          ? "bg-status-completed/15 text-status-completed"
                          : isProgress
                          ? "bg-primary/10 text-primary"
                          : "bg-surface-container text-outline"
                      }`}
                    >
                      <span className="material-symbols-outlined font-medium align-middle select-none">
                        {module.iconName || "school"}
                      </span>
                    </div>
                    <h3 className="text-lg md:text-xl font-headline font-bold text-on-surface">
                      {module.title}
                    </h3>
                  </div>

                  {/* Progressive visual metrics */}
                  {isProgress && (
                    <div className="mt-2 mb-4 space-y-1">
                      <div className="w-full bg-surface-container-low rounded-full h-2">
                        <div
                          className="bg-primary h-2 rounded-full transition-all duration-500"
                          style={{ width: `${module.progress}%` }}
                        />
                      </div>
                      <div className="flex justify-between items-center text-[10px] font-mono font-bold text-outline">
                        <span>Progress Metrics</span>
                        <span className="text-primary">{module.progress}% Complete</span>
                      </div>
                    </div>
                  )}

                  {isCompleted && (
                    <div className="mt-1 mb-4 flex items-center gap-1 text-[11px] font-headline font-bold text-status-completed uppercase tracking-wider">
                      <span className="material-symbols-outlined" style={{ fontSize: '16px' }}>task_alt</span>
                      Module Conquered
                    </div>
                  )}

                  {isLocked && (
                    <div className="mt-1 mb-3 flex items-center gap-1 text-[11px] font-headline font-bold text-outline uppercase tracking-wider">
                      <span className="material-symbols-outlined" style={{ fontSize: '16px' }}>lock_open</span>
                      Locked Segment
                    </div>
                  )}

                  {/* Primary interactive controls */}
                  <div className="flex items-center gap-3 mt-auto pt-3 border-t border-outline-variant/30">
                    {!isLocked ? (
                      <button
                        onClick={() => onExploreNode(module.id)}
                        className={`text-[11px] font-headline font-bold flex items-center gap-1 hover:underline select-none border-0 bg-transparent py-1 ${
                          isCompleted ? "text-status-completed" : "text-primary"
                        }`}
                      >
                        <span className="material-symbols-outlined" style={{ fontSize: '16px' }}>library_books</span>
                        Explore Lessons
                      </button>
                    ) : (
                      <span className="text-[11px] font-headline font-medium text-outline flex items-center gap-1">
                        <span className="material-symbols-outlined" style={{ fontSize: '14px' }}>lock</span>
                        Locked
                      </span>
                    )}

                    {!isLocked && (
                      <button
                        onClick={() =>
                          setActiveAccordionId(activeAccordionId === module.id ? null : module.id)
                        }
                        className="text-[11px] font-headline font-bold ml-auto text-on-surface-variant hover:text-on-surface select-none border-0 bg-transparent py-1 flex items-center gap-0.5"
                      >
                        <span className="material-symbols-outlined" style={{ fontSize: '16px' }}>checklist</span>
                        Checklist ({module.tasks.filter((t) => t.completed).length}/{module.tasks.length})
                        <span
                          className={`material-symbols-outlined transition-transform ${
                            activeAccordionId === module.id ? "rotate-180" : ""
                          }`}
                          style={{ fontSize: '14px' }}
                        >
                          expand_more
                        </span>
                      </button>
                    )}
                  </div>

                  {/* Expandable tasks checklist panel */}
                  {!isLocked && activeAccordionId === module.id && (
                    <div className="mt-4 pt-4 border-t border-outline-variant/30 space-y-3 bg-surface-container-low/50 p-4 rounded-lg animate-fade-in">
                      <h4 className="text-[11px] font-headline font-bold text-on-surface uppercase tracking-wider">
                        {isCompleted ? "Completed Tasks" : "Active Exercises"}
                      </h4>
                      <div className="space-y-2">
                        {module.tasks.map((task) => (
                          <label
                            key={task.id}
                            className="flex items-start gap-2.5 cursor-pointer text-[11px] font-headline font-medium text-on-surface-variant hover:text-on-surface select-none"
                          >
                            <input
                              type="checkbox"
                              checked={task.completed}
                              onChange={() => handleToggleTask(module, task.id)}
                              className="w-4 h-4 text-primary bg-surface-container border-outline-variant rounded focus:ring-primary focus:ring-2 mt-0.5"
                            />
                            <span className={task.completed ? "line-through text-outline/80" : ""}>
                              {task.title}
                            </span>
                          </label>
                        ))}
                      </div>

                      {isProgress && module.progress < 100 && (
                        <div className="bg-primary/5 p-2 rounded text-[10px] font-headline text-primary font-medium text-center">
                          Complete all exercises to unlock the subsequent module!
                        </div>
                      )}
                    </div>
                  )}
                </div>
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}
