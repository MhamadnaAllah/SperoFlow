"use client";

import React, { useState } from 'react';

const faqs = [
  {
    q: "What is the Approval-First guarantee?",
    a: "Every AI suggestion — whether it's a task classification, schedule change, or priority adjustment — requires your explicit approval before it takes effect. The AI proposes, you decide. No automated changes are ever made without your consent."
  },
  {
    q: "How does the Quadrant 2 methodology work?",
    a: "Based on the Eisenhower/Covey Matrix, tasks are classified into four quadrants. Quadrant 2 (Important but Not Urgent) is where strategic growth happens. Our AI actively surfaces Q2 tasks and helps you protect time for them, preventing the tyranny of the urgent."
  },
  {
    q: "How is my data protected?",
    a: "All personal data is encrypted at rest and in transit using AES-256 and TLS 1.3. Journaling sentiment analysis runs on-device when possible. We never sell or share your data with third parties. Zero third-party data leaks — guaranteed."
  },
  {
    q: "What can the AI Coach actually do?",
    a: "The AI Coach parses natural language brain dumps into structured tasks, classifies them into Covey quadrants, considers your energy levels and OKRs for prioritization, and provides gentle coaching during reflection sessions. It's both a productivity strategist and a well-being supporter."
  },
  {
    q: "What's included in the free tier?",
    a: "The Starter plan includes 5 active projects, basic Covey Matrix visualization, daily journaling, manual task prioritization, and community support. It's free forever with no credit card required."
  }
];

const FaqSection = () => {
  const [openIndex, setOpenIndex] = useState(null);

  const toggleOpen = (index) => {
    setOpenIndex(openIndex === index ? null : index);
  };

  return (
    <section className="py-24 bg-surface px-6 lg:px-12">
      <div className="max-w-4xl mx-auto">
        <h2 className="text-display-lg text-on-background mb-12 text-center">
          Frequently Asked Questions
        </h2>
        
        <div className="space-y-4">
          {faqs.map((faq, index) => {
            const isOpen = openIndex === index;
            return (
              <div 
                key={index} 
                className="bg-surface-container-lowest rounded-3xl ghost-border overflow-hidden"
              >
                <button
                  className="w-full flex items-center justify-between p-6 text-left text-title-md font-medium text-on-background focus:outline-none"
                  onClick={() => toggleOpen(index)}
                  aria-expanded={isOpen}
                >
                  <span>{faq.q}</span>
                  <span 
                    className={`material-symbols-outlined faq-chevron transition-transform duration-300 ${
                      isOpen ? 'rotate-180' : ''
                    }`}
                  >
                    expand_more
                  </span>
                </button>
                
                <div 
                  className={`faq-content transition-all duration-300 ease-in-out ${
                    isOpen ? 'max-h-96 opacity-100' : 'max-h-0 opacity-0'
                  } overflow-hidden`}
                >
                  <div className="px-6 pb-6 text-body-md text-on-surface-variant">
                    {faq.a}
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      </div>
    </section>
  );
};

export default FaqSection;
