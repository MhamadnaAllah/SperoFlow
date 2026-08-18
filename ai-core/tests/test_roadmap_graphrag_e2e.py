"""
Unit and Integration Tests for GraphRAG Roadmap Generation, Resource Extraction, and Topological Sorting.
"""

from speroflow_ai.routers.roadmap import (
    _clean_topic_name,
    _topological_sort,
    _extract_markdown_resources,
    _extract_topic_overview,
    _retrieve_local_roadmap_dataset,
    _build_fallback_timeline,
    SYSTEM_PROMPT,
)
from speroflow_ai.models.responses import LearningStep, LearningTimeline


def test_clean_topic_name():
    assert _clean_topic_name("i want to learn Rust Programming") == "Rust Programming"
    assert _clean_topic_name("how to learn React") == "React"
    assert _clean_topic_name("guide to Game Development") == "Game Development"
    assert _clean_topic_name("Frontend") == "Frontend"


def test_extract_markdown_resources():
    sample_md = """
# What is C++?
C++ is a high-performance general purpose language.

Visit the following resources to learn more:
- [@article@Learn C++](https://www.learncpp.com/)
- [@video@C++ Full Course](https://youtu.be/vLnPwxZdW4Y)
- [@course@Modern Cpp Series](https://www.youtube.com/playlist?list=123)
- [CppReference](https://en.cppreference.com)
"""
    resources = _extract_markdown_resources(sample_md)
    assert len(resources) == 4
    assert "[Article] Learn C++ - https://www.learncpp.com/" in resources
    assert "[Video] C++ Full Course - https://youtu.be/vLnPwxZdW4Y" in resources
    assert "[Course] Modern Cpp Series - https://www.youtube.com/playlist?list=123" in resources
    assert "[Article] CppReference - https://en.cppreference.com" in resources


def test_extract_topic_overview():
    sample_md = """# Setting up your Environment

Setting up your environment in C++ involves configuring your compiler (like GCC or Clang), IDE, and build system. Proper setup is key to a smooth workflow.

Visit the following resources to learn more:
- [@article@C++ Getting Started](https://www.w3schools.com/cpp/cpp_getstarted.asp)
"""
    overview = _extract_topic_overview(sample_md)
    assert "Setting up your environment in C++ involves configuring your compiler" in overview
    assert "Visit the following resources" not in overview


def test_retrieve_local_roadmap_dataset_cpp():
    topics, edges = _retrieve_local_roadmap_dataset("Learn Modern C++")
    assert len(topics) > 10
    titles = [t["title"] for t in topics]
    assert any("C++" in t or "Environment" in t or "Pointers" in t for t in titles)
    
    # Check that resources are extracted
    has_resources = any(len(t.get("resources", [])) > 0 for t in topics)
    assert has_resources


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
    sample_topics = [
        {
            "title": "Introduction to Language",
            "overview": "C++ is a high performance language.",
            "resources": ["[Article] Learn C++ - https://www.learncpp.com/"],
        },
        {
            "title": "Setting up your Environment",
            "overview": "Configure GCC, Clang, and IDE.",
            "resources": ["[Video] Setup VS Code - https://youtube.com/watch?v=123"],
        },
        {
            "title": "Pointers and References",
            "overview": "Understand memory models and lifetimes.",
            "resources": ["[Article] Pointers in C++ - https://en.cppreference.com"],
        },
        {
            "title": "Standard Library + STL",
            "overview": "Containers, Iterators, and Algorithms.",
            "resources": ["[Docs] STL Guide - https://en.cppreference.com/w/cpp/container"],
        },
    ]
    timeline = _build_fallback_timeline(
        "C++ Systems Engineering",
        sample_topics,
        ["[Article] C++ Reference - https://en.cppreference.com"]
    )
    
    assert timeline["goal"] == "C++ Systems Engineering"
    assert len(timeline["steps"]) >= 4
    assert timeline["total_estimated_hours"] > 0
    assert "C++ Systems Engineering" in timeline["motivational_summary"] or "C++" in timeline["motivational_summary"]
    
    # Check that google search is NOT in any resources
    for s in timeline["steps"]:
        for r in s["resources"]:
            assert "google.com/search" not in r

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
    assert res.goal == "C++ Systems Engineering"
    assert len(res.steps) >= 4
