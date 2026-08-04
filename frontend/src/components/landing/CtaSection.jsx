"use client";

import React from 'react';
import { useRouter } from 'next/navigation';

const CtaSection = () => {
  const router = useRouter();

  return (
    <section className="py-32 px-6 lg:px-8 bg-surface-bright text-center">
      <div 
        className="max-w-3xl mx-auto space-y-8 glass-panel p-12 rounded-[3rem] border-none relative overflow-hidden ambient-shadow"
        data-reveal
      >
        <div className="absolute inset-0 bg-gradient-to-br from-primary/5 to-secondary/5 opacity-50" />
        
        <div className="relative z-10">
          <div className="w-16 h-16 mx-auto bg-primary rounded-2xl flex items-center justify-center text-on-primary shadow-lg shadow-primary/20 mb-6">
            <span className="material-symbols-outlined text-[32px]">architecture</span>
          </div>
          
          <h2 className="text-3xl md:text-5xl font-bold tracking-tight text-on-background mb-6 font-headline">
            Ready to harmonize your life?
          </h2>
          
          <p className="text-lg text-on-surface-variant mb-10 max-w-xl mx-auto font-light">
            Join thousands of high-performers who have upgraded to a comprehensive life operating system that honors both ambition and well-being.
          </p>
          
          <button 
            onClick={() => router.push('/signup')}
            className="bg-primary hover:bg-primary-dim text-white text-lg font-medium px-10 py-4 rounded-full shadow-lg shadow-primary/20 transition-all hover:scale-105 active:scale-95 flex items-center justify-center gap-2 mx-auto"
          >
            <span>Enter the Studio</span>
            <span className="material-symbols-outlined">rocket_launch</span>
          </button>
        </div>
      </div>
    </section>
  );
};

export default CtaSection;
