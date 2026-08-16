"""
Unit and Integration Tests for GraphRAG Roadmap Generation and Kahn Topological Sorting.
"""

from speroflow_ai.routers.roadmap import (
    _clean_topic_name,
    _topological_sort,
    _build_fallback_timeline,
    SYSTEM_PROMPT,
)
from speroflow_ai.models.responses import LearningStep, LearningTimeline


def test_clean_topic_name():
    assert _clean_topic_name("i want to learn Rust Programming") == "Rust Programming"
    assert _clean_topic_name("how to learn React") == "React"
    assert _clean_topic_name("guide to Game Development") == "Game Development"
    assert _clean_topic_name("Frontend") == "Frontend"


def test_topological_sort_kahn_dag():
    nodes = ["React", "JavaScript", "HTML", "Next.js", "CSS"]
    edges = [
        ("HTML", "JavaScript"),
        ("CSS", "JavaScript"),
        ("JavaScript", "React"),
        ("React", "Next.js"),
    ]
    sorted_nodes = _topological_sort(nodes, edges)
    
    # Prerequisite verification
    assert sorted_nodes.index("HTML") < sorted_nodes.index("JavaScript")
    assert sorted_nodes.index("CSS") < sorted_nodes.index("JavaScript")
    assert sorted_nodes.index("JavaScript") < sorted_nodes.index("React")
    assert sorted_nodes.index("React") < sorted_nodes.index("Next.js")
    assert len(sorted_nodes) == 5


def test_build_fallback_timeline_schema():
    timeline = _build_fallback_timeline(
        "Rust Systems Engineering",
        ["Ownership", "Borrowing", "Lifetimes", "Cargo"],
        ["https://doc.rust-lang.org/book/ - Rust Official Manual"]
    )
    
    assert timeline["goal"] == "Rust Systems Engineering"
    assert len(timeline["steps"]) == 5
    assert timeline["total_estimated_hours"] > 0
    assert "Rust" in timeline["motivational_summary"]

    # Verify Pydantic validation
    steps_models = [
        LearningStep(
            topic=s["topic"],
            description=s["description"],
            estimated_hours=float(s["estimated_hours"]),
            resources=s["resources"],
        )
        for s in timeline["steps"]
    ]
    res = LearningTimeline(
        goal=timeline["goal"],
        steps=steps_models,
        total_estimated_hours=timeline["total_estimated_hours"],
        motivational_summary=timeline["motivational_summary"],
    )
    assert res.goal == "Rust Systems Engineering"
    assert len(res.steps) == 5
