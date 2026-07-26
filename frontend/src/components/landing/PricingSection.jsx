"use client";

import React, { useState } from 'react';

const PricingSection = () => {
  const [isAnnual, setIsAnnual] = useState(true);

  return (
    <section className="py-24 bg-surface-container-low px-6 lg:px-12" id="pricing">
      <div className="max-w-7xl mx-auto">
        <div className="text-center max-w-3xl mx-auto mb-12">
          <h2 className="text-display-lg text-on-background mb-4">Simple, transparent pricing.</h2>
          <p className="text-title-md text-on-surface-variant font-light">
            Choose the plan that fits your workflow.
          </p>
        </div>

        <div className="flex items-center justify-center gap-4 mb-16">
          <span className={`text-label-md font-medium ${!isAnnual ? 'text-on-background' : 'text-on-surface-variant'}`}>
            Monthly
          </span>
          <button
            type="button"
            className={`w-12 h-6 rounded-full relative transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 ${
              isAnnual ? 'bg-primary' : 'bg-surface-container-high'
            }`}
            onClick={() => setIsAnnual(!isAnnual)}
            aria-pressed={isAnnual}
          >
            <span className="sr-only">Toggle Annual Billing</span>
            <span
              className={`block w-5 h-5 rounded-full bg-white shadow transform transition-transform ${
                isAnnual ? 'translate-x-6' : 'translate-x-1'
              } mt-0.5`}
            />
          </button>
          <div className="flex items-center gap-2">
            <span className={`text-label-md font-medium ${isAnnual ? 'text-on-background' : 'text-on-surface-variant'}`}>
              Annual
            </span>
            <span className="bg-secondary/10 text-secondary text-xs font-semibold px-2 py-0.5 rounded-full">
              Save 20%
            </span>
          </div>
        </div>

        <div className="grid lg:grid-cols-3 gap-8 mt-16">
          {/* Starter */}
          <div className="bg-surface-container-lowest rounded-3xl p-8 ambient-shadow ghost-border flex flex-col">
            <h3 className="text-title-lg font-bold text-on-background mb-2">Starter</h3>
            <div className="text-display-lg font-bold text-on-background mb-1">
              $0
            </div>
            <p className="text-sm text-on-surface-variant mb-6">Free forever</p>
            <button className="w-full py-3 px-6 rounded-full border-2 border-primary text-primary font-medium hover:bg-primary/5 transition-colors mb-8">
              Get Started Free
            </button>
            <div className="space-y-4 flex-1">
              {["5 Active Projects", "Basic Covey Matrix", "Daily Journal", "Manual Prioritization", "Community Support"].map((feature, i) => (
                <div key={i} className="flex items-start gap-3">
                  <span className="material-symbols-outlined text-secondary text-[18px]">check_circle</span>
                  <span className="text-sm text-on-surface-variant">{feature}</span>
                </div>
              ))}
            </div>
          </div>

          {/* Pro AI Coach */}
          <div className="bg-surface-container-lowest rounded-3xl p-8 ambient-shadow ghost-border flex flex-col ring-2 ring-primary transform lg:-translate-y-4 relative">
            <div className="absolute top-0 left-1/2 transform -translate-x-1/2 -translate-y-1/2 bg-primary text-on-primary text-xs font-bold px-3 py-1 rounded-full uppercase tracking-wider">
              Most Popular
            </div>
            <h3 className="text-title-lg font-bold text-on-background mb-2">Pro AI Coach</h3>
            <div className="flex items-end gap-1 mb-1">
              <span className="text-display-lg font-bold text-on-background">
                ${isAnnual ? '12' : '15'}
              </span>
              <span className="text-on-surface-variant mb-2">/mo</span>
            </div>
            <p className="text-sm text-on-surface-variant mb-6">
              Billed {isAnnual ? 'annually' : 'monthly'}
            </p>
            <button className="w-full py-3 px-6 rounded-full bg-primary hover:bg-primary-dim text-white font-medium shadow-lg shadow-primary/20 transition-all hover:scale-[1.02] active:scale-[0.98] mb-8">
              Start Free Trial
            </button>
            <div className="space-y-4 flex-1">
              {["Unlimited Projects", "AI-Powered Covey Matrix", "Advanced Journaling & Insights", "Energy-Aware Scheduling", "Personal AI Coach", "OKR Tracking", "Priority Support"].map((feature, i) => (
                <div key={i} className="flex items-start gap-3">
                  <span className="material-symbols-outlined text-secondary text-[18px]">check_circle</span>
                  <span className="text-sm text-on-surface-variant">{feature}</span>
                </div>
              ))}
            </div>
          </div>

          {/* Teams & Enterprise */}
          <div className="bg-surface-container-lowest rounded-3xl p-8 ambient-shadow ghost-border flex flex-col">
            <h3 className="text-title-lg font-bold text-on-background mb-2">Teams & Enterprise</h3>
            <div className="text-display-lg font-bold text-on-background mb-1">
              Custom
            </div>
            <p className="text-sm text-on-surface-variant mb-6">For larger organizations</p>
            <button className="w-full py-3 px-6 rounded-full border-2 border-on-surface-variant text-on-surface-variant font-medium hover:bg-on-surface-variant/5 transition-colors mb-8">
              Contact Sales
            </button>
            <div className="space-y-4 flex-1">
              {["Everything in Pro", "Team Workspaces", "Admin Dashboard", "SSO & SAML", "Dedicated Account Manager", "Custom Integrations", "SLA Guarantee"].map((feature, i) => (
                <div key={i} className="flex items-start gap-3">
                  <span className="material-symbols-outlined text-secondary text-[18px]">check_circle</span>
                  <span className="text-sm text-on-surface-variant">{feature}</span>
                </div>
              ))}
            </div>
          </div>
        </div>
      </div>
    </section>
  );
};

export default PricingSection;
