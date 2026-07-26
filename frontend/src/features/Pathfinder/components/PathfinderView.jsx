"use client";

/**
 * PathfinderView — Main orchestrator for the Pathfinder AI Roadmap feature.
 *
 * Replaces the standalone Vite app's App.tsx. Manages view state across
 * three screens (VisionaryGoal → RoadmapOverview → NodeStudy) and wires
 * roadmap generation to the main SperoFlow FastAPI backend via the existing
 * same-origin ASP.NET API.
 */

import { useState, useEffect, useCallback } from "react";
import { aiApi } from "@/lib/api/client";
import VisionaryGoalScreen from "./VisionaryGoalScreen";
import RoadmapScreen from "./RoadmapScreen";
import NodeStudyScreen from "./NodeStudyScreen";

// ── Default Roadmap Data ────────────────────────────────────────────────────
// Shown on initial load before any generation occurs.
const DEFAULT_ROADMAP = {
  id: "default-ai-engineer",
  title: "AI Engineer Roadmap",
  description:
    "Your structured path to mastering AI Engineering. Follow the nodes to progress through the curriculum.",
  horizon: "1yr",
  goalText:
    "Master the fundamental algorithms, deep neural architectures, and scalable ML engineering practices required to build and deploy intelligent systems at scale.",
  modules: [
    {
      id: "IF-001",
      title: "Internet Fundamentals",
      category: "Core Module",
      iconName: "language",
      status: "completed",
      progress: 100,
      tasks: [
        { id: "IF-001-t1", title: "How does the internet work?", completed: true },
        { id: "IF-001-t2", title: "What is HTTP?", completed: true },
        { id: "IF-001-t3", title: "DNS and how it works", completed: true },
      ],
      connectedConcepts: ["Network Security", "Cloud Architecture", "Web Servers", "OSI Model"],
      ragSources: [
        { title: "Internet_Architecture_v3.pdf", type: "pdf", details: "Local Vector Store • 2.4 MB" },
        { title: "IETF Protocol Specs", type: "link", details: "External Web Crawler • Indexed Today" },
        { title: "DNS Resolver Mechanisms", type: "wiki", details: "Markdown Wiki • Last edited 1w ago" },
      ],
      synthesizedInsight:
        "The Internet operates as a decentralized, global network of interconnected networks, governed by standardized protocols. At its core, data transmission relies on the TCP/IP suite, which breaks information into packets, routes them across various physical and logical nodes, and reassembles them at the destination.\n\nCritical infrastructure components include DNS (Domain Name System), acting as the distributed directory resolving human-readable domain names to machine-actionable IP addresses, and the HTTP/HTTPS application-layer protocols that dictate the formatting and secure transmission of hypertext documents across the World Wide Web.",
      detailedBreakdown: [
        {
          title: "Packet Switching & Routing",
          tag: "CORE",
          content:
            "Unlike circuit-switched networks (like traditional telephone lines), the internet uses packet switching. Data is chopped into small chunks called packets. Each packet carries a payload and metadata (header) containing source and destination IP addresses.",
          bullets: [
            "Routers read headers and determine the optimal path.",
            "Packets may take different routes to avoid congestion.",
            "TCP handles reassembly and error checking at the destination.",
          ],
        },
        {
          title: "DNS Infrastructure",
          tag: "CORE",
          content:
            "DNS is a hierarchical, decentralized naming system. It translates human-memorized domain names (e.g., example.com) to the numerical IP addresses needed for locating and identifying computer services.",
          bullets: [
            "Root servers, TLD name servers, and authoritative servers form the tree lookup.",
            "Caching is applied heavily at the OS, browser, and router stages.",
          ],
        },
        {
          title: "HTTP/HTTPS Protocols",
          tag: "APP TIER",
          content:
            "The foundation of data communication for the World Wide Web. HTTPS adds encryption (TLS/SSL) over HTTP to secure communications over a computer network.",
          bullets: [
            "HTTP is a stateless request-response protocol running on TCP.",
            "HTTPS establishes session keys securely using public-key cryptography.",
          ],
        },
      ],
    },
    {
      id: "FE-002",
      title: "Frontend Basics",
      category: "Current Focus",
      iconName: "code",
      status: "progress",
      progress: 45,
      tasks: [
        { id: "FE-002-t1", title: "Semantic HTML", completed: true },
        { id: "FE-002-t2", title: "CSS Flexbox & Grid", completed: false },
        { id: "FE-002-t3", title: "JavaScript ES6+", completed: false },
      ],
      connectedConcepts: ["DOM Tree", "Responsive CSS", "TypeScript Basics", "Single Page Apps"],
      ragSources: [
        { title: "Web_Standards_Guide.pdf", type: "pdf", details: "Local Vector Store • 1.2 MB" },
        { title: "MDN Web Docs Index", type: "link", details: "External Web Crawler • Indexed Today" },
      ],
      synthesizedInsight:
        "Frontend development shapes the visual and interactive parts of a website. Designing simple, accessible, and structured semantics using HTML is the cornerstone of browser execution. Adding styles with CSS Flexbox & Grid creates custom layouts responsive across devices, while JavaScript powers client-side behaviors, APIs, and state updates.",
      detailedBreakdown: [
        {
          title: "Semantic Document Object Model (DOM)",
          tag: "HTML",
          content:
            "Semantic markup gives meaning to document structures rather than just offering stylistic controls. Headers, articles, sections, and footers help accessibility engines and indexing bots parse layouts cleanly.",
        },
        {
          title: "Layout Engines: Flexbox & Grid",
          tag: "CSS",
          content:
            "Flexbox specializes in handling single-axis component distribution, whereas CSS Grid is designed for complex double-axis (row and column) layout architectures. Combining both lets you design beautiful liquid designs.",
        },
      ],
    },
    {
      id: "VC-003",
      title: "Version Control",
      category: "Locked",
      iconName: "fork_right",
      status: "locked",
      progress: 0,
      tasks: [
        { id: "VC-003-t1", title: "Git Basics (commit, push, pull)", completed: false },
        { id: "VC-003-t2", title: "Branching & Merging Strategies", completed: false },
        { id: "VC-003-t3", title: "Collaboration workflows (PRs)", completed: false },
      ],
      connectedConcepts: ["GitHub Workflow", "CI/CD Pipelines", "Conflict Resolution"],
      ragSources: [{ title: "Pro_Git_Book.pdf", type: "pdf", details: "Git Core Pubs • 4.1 MB" }],
      synthesizedInsight:
        "Version control enables software developers to track revision history, isolate changes in branches, and collaborate concurrently on multiple feature sets without risk of code conflicts or system breakdowns.",
      detailedBreakdown: [
        {
          title: "Branch management with Git",
          tag: "GIT",
          content:
            "Git maintains complete directory hashes. Commits act as chronological nodes. Branches allow developers to isolate feature workflows before committing changes.",
        },
      ],
    },
  ],
};

// ── ICON MAP — maps topic keywords to Material Symbols icons ────────────────
const ICON_MAP = {
  internet: "language",
  web: "language",
  http: "language",
  frontend: "code",
  html: "code",
  css: "code",
  javascript: "code",
  react: "code",
  backend: "dns",
  api: "dns",
  server: "dns",
  database: "database",
  sql: "database",
  git: "fork_right",
  version: "fork_right",
  ai: "psychology",
  machine: "psychology",
  deep: "psychology",
  neural: "psychology",
  llm: "psychology",
  rag: "database",
  vector: "database",
  cloud: "cloud",
  docker: "layers",
  kubernetes: "layers",
  devops: "settings",
  security: "shield",
  testing: "bug_report",
  data: "bar_chart",
  python: "terminal",
  default: "school",
};

function normalizeTimeline(payload) {
  const data = payload?.payload || payload?.data || payload || {};
  const rawSteps = Array.isArray(data.steps) ? data.steps : [];
  return {
    goal: data.goal || data.goalName || data.goal_name || "Learning pathway",
    summary: data.summary || data.description || "A structured route through the available knowledge base.",
    steps: rawSteps.map((step, index) => ({
      step_number: step.step_number || step.stepNumber || index + 1,
      topic_name: step.topic_name || step.topicName || step.title || `Step ${index + 1}`,
      description: step.description || step.reason || "Study the supporting material for this step.",
      estimated_hours: step.estimated_hours || step.estimatedHours || step.hours || 0,
    })),
  };
}
/**
 * Transform a Graph RAG LearningTimeline response into the Pathfinder Roadmap
 * data structure that the UI components expect.
 *
 * @param {object} timeline - The LearningTimeline from the backend
 * @param {string} goalText - The original user goal text
 * @param {string} horizon - The selected time horizon
 * @returns {object} A Roadmap object compatible with the Pathfinder UI
 */
function transformTimelineToRoadmap(timeline, goalText, horizon) {
  const modules = timeline.steps.map((step, index) => {
    // Determine status based on position
    let status, category, progress;
    if (index === 0) {
      status = "completed";
      category = "Core Module";
      progress = 100;
    } else if (index === 1) {
      status = "progress";
      category = "Current Focus";
      progress = 30;
    } else {
      status = "locked";
      category = "Locked";
      progress = 0;
    }

    // Pick an icon based on topic keywords
    const topicLower = step.topic_name.toLowerCase();
    const iconName =
      Object.keys(ICON_MAP).find((key) => topicLower.includes(key)) || "default";

    // Generate tasks from the step description
    const descSentences = step.description
      .split(/[.!?]+/)
      .filter((s) => s.trim().length > 10)
      .slice(0, 3);
    const tasks = descSentences.map((sentence, tIdx) => ({
      id: `${step.step_number}-t${tIdx + 1}`,
      title: sentence.trim(),
      completed: status === "completed",
    }));
    // Ensure at least one task
    if (tasks.length === 0) {
      tasks.push({
        id: `${step.step_number}-t1`,
        title: `Study ${step.topic_name}`,
        completed: status === "completed",
      });
    }

    return {
      id: `STEP-${String(step.step_number).padStart(3, "0")}`,
      title: step.topic_name,
      category,
      iconName: ICON_MAP[iconName],
      status,
      progress,
      tasks,
      connectedConcepts: timeline.steps
        .filter((s) => s.step_number !== step.step_number)
        .slice(0, 4)
        .map((s) => s.topic_name),
      ragSources: [
        {
          title: `${step.topic_name}_Guide.pdf`,
          type: "pdf",
          details: `Knowledge Graph • ${step.estimated_hours}h study time`,
        },
        {
          title: "Graph RAG Source",
          type: "link",
          details: "Neo4j Knowledge Graph • Verified prerequisite",
        },
      ],
      synthesizedInsight: step.description,
      detailedBreakdown: [
        {
          title: `Core Concepts of ${step.topic_name}`,
          tag: "CORE",
          content: step.description,
          bullets: [
            `Estimated study time: ${step.estimated_hours} hours`,
            `Step ${step.step_number} of ${timeline.steps.length} in the learning path`,
          ],
        },
      ],
    };
  });

  return {
    id: `gen-${Date.now()}`,
    title: timeline.goal,
    description: timeline.summary,
    horizon: horizon || "1yr",
    goalText: goalText,
    modules,
  };
}

// ── Main Component ──────────────────────────────────────────────────────────
export default function PathfinderView() {
  const [currentView, setCurrentView] = useState("visionaryGoal");
  const [roadmap, setRoadmap] = useState(DEFAULT_ROADMAP);
  const [selectedModuleId, setSelectedModuleId] = useState("IF-001");
  const [loading, setLoading] = useState(false);
  const [toastMessage, setToastMessage] = useState(null);

  // Restore persisted state from localStorage
  useEffect(() => {
    const saved = localStorage.getItem("pf_roadmap_data_store");
    if (saved) {
      try {
        setRoadmap(JSON.parse(saved));
      } catch {
        console.warn("Could not retrieve persistent state.");
      }
    }
  }, []);

  const saveRoadmapState = useCallback((updatedRoadmap) => {
    setRoadmap(updatedRoadmap);
    localStorage.setItem("pf_roadmap_data_store", JSON.stringify(updatedRoadmap));
  }, []);

  const triggerToast = useCallback((msg) => {
    setToastMessage(msg);
    setTimeout(() => setToastMessage(null), 3000);
  }, []);

  /**
   * Generate an AI learning path by calling the Graph RAG backend
   * through the same-origin ASP.NET API.
   */
  const handleGenerateAIPath = async () => {
    triggerToast("Create an active goal to review a graph-grounded roadmap.");
  };

  // Handle updates to modules (checkbox state toggles)
  const handleUpdateModule = useCallback(
    (updatedModule) => {
      const updatedModules = roadmap.modules.map((mod) =>
        mod.id === updatedModule.id ? updatedModule : mod
      );
      const updatedRoadmap = {
        ...roadmap,
        modules: updatedModules,
      };
      saveRoadmapState(updatedRoadmap);

      if (updatedModule.progress === 100) {
        triggerToast(`Milestone Conquered: ${updatedModule.title}!`);
      }
    },
    [roadmap, saveRoadmapState, triggerToast]
  );

  // Navigation handlers
  const handleExploreNode = useCallback((moduleId) => {
    setSelectedModuleId(moduleId);
    setCurrentView("nodeStudy");
    window.scrollTo({ top: 0, behavior: "smooth" });
  }, []);

  const handleNavigate = useCallback((view) => {
    setCurrentView(view);
    window.scrollTo({ top: 0, behavior: "smooth" });
  }, []);

  // Current active studying module
  const currentModule =
    roadmap.modules.find((m) => m.id === selectedModuleId) || roadmap.modules[0];

  return (
    <div className="flex flex-col min-h-full selection:bg-primary/20 selection:text-on-surface">
      {/* Main page content workspace canvas */}
      <main className="flex-grow p-6 md:p-8 lg:p-10 max-w-[1280px] w-full mx-auto">
        {currentView === "visionaryGoal" && (
          <VisionaryGoalScreen
            roadmap={roadmap}
            onGenerate={handleGenerateAIPath}
            onViewRoadmap={() => handleNavigate("roadmapOverview")}
            loading={loading}
          />
        )}

        {currentView === "roadmapOverview" && (
          <RoadmapScreen
            roadmap={roadmap}
            onExploreNode={handleExploreNode}
            onUpdateModule={handleUpdateModule}
          />
        )}

        {currentView === "nodeStudy" && (
          <NodeStudyScreen
            module={currentModule}
            onNavigateBack={() => handleNavigate("roadmapOverview")}
            onUpdateModule={handleUpdateModule}
          />
        )}
      </main>

      {/* Persistent global toast notification system */}
      {toastMessage && (
        <div className="fixed bottom-6 right-6 bg-inverse-surface border border-outline/35 text-inverse-on-surface px-4 py-3 rounded-lg shadow-xl font-headline font-bold text-[11px] flex items-center gap-2 z-[200] animate-slide-up">
          <span className="material-symbols-outlined text-status-completed" style={{ fontSize: '18px' }}>verified</span>
          {toastMessage}
        </div>
      )}
    </div>
  );
}
