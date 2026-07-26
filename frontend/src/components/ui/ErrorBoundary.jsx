"use client";

import React from "react";

export default class ErrorBoundary extends React.Component {
  constructor(props) {
    super(props);
    this.state = { hasError: false, error: null };
  }

  static getDerivedStateFromError(error) {
    return { hasError: true, error };
  }

  componentDidCatch(error, errorInfo) {
    console.error("ErrorBoundary caught an error:", error, errorInfo);
  }

  render() {
    if (this.state.hasError) {
      return (
        <div className="flex flex-col items-center justify-center p-10 m-6 bg-error-container/20 border border-error/30 rounded-3xl text-center">
          <span className="material-symbols-outlined text-error text-5xl mb-4">
            warning
          </span>
          <h2 className="text-xl font-bold text-on-surface mb-2">Something went wrong</h2>
          <p className="text-sm text-on-surface-variant max-w-md mb-6">
            {this.state.error?.message || "An unexpected error occurred in this component. Please try refreshing."}
          </p>
          <button
            onClick={() => this.setState({ hasError: false, error: null })}
            className="px-6 py-2 bg-error text-on-error rounded-xl text-sm font-bold shadow-md hover:opacity-90 transition-opacity"
          >
            Try Again
          </button>
        </div>
      );
    }

    return this.props.children;
  }
}
