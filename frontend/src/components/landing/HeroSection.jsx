"use client";

import { useRouter } from "next/navigation";
import HeroShaderCanvas from "./HeroShaderCanvas";

export default function HeroSection() {
  const router = useRouter();

  return (
    <section className="relative min-h-[90vh] flex items-center justify-center pt-24 pb-32 px-6 lg:px-12 overflow-hidden">
      <HeroShaderCanvas />
      
      <div className="absolute top-[-10%] right-[-10%] w-[600px] h-[600px] rounded-full blur-[120px] mix-blend-multiply bg-primary-container opacity-40 pointer-events-none" />
      <div className="absolute bottom-[-10%] left-[-10%] w-[600px] h-[600px] rounded-full blur-[120px] mix-blend-multiply bg-secondary-container opacity-30 pointer-events-none" />
      
      <div className="relative z-10 max-w-4xl mx-auto text-center space-y-8" data-reveal>
        <div className="inline-flex items-center gap-2 px-4 py-1.5 rounded-full glass-panel">
          <div className="w-2 h-2 rounded-full bg-secondary animate-pulse" />
          <span className="text-xs font-medium tracking-wide text-secondary uppercase">AI Coach & Sanctuary</span>
        </div>
        
        <h1 className="text-6xl md:text-7xl lg:text-8xl font-black tracking-tighter text-on-surface leading-[1.1] font-headline">
          Master Your Mission. <br />
          <span className="text-primary">Find Your Peace.</span>
        </h1>
        
        <p className="text-lg md:text-xl text-on-surface-variant max-w-2xl mx-auto leading-relaxed font-light">
          Your AI Coach acts as both a ruthless strategist for your ambitions and a gentle supporter for your mental well-being. Achieve high performance without the burnout.
        </p>
        
        <div className="pt-8 flex flex-col sm:flex-row items-center justify-center gap-4">
          <button 
            onClick={() => router.push("/auth")}
            className="group flex items-center justify-center gap-2 px-8 py-4 bg-on-background hover:bg-slate-800 text-white font-medium rounded-full shadow-lg transition-all"
          >
            Begin Your Journey
            <span className="material-symbols-outlined transition-transform duration-300 group-hover:translate-x-1">arrow_forward</span>
          </button>
          
          <a 
            href="#methodology"
            className="flex items-center justify-center gap-2 px-8 py-4 bg-white text-on-surface font-medium rounded-full ambient-shadow hover:bg-surface-variant transition-colors"
          >
            <span className="material-symbols-outlined">play_circle</span>
            See it in action
          </a>
        </div>
      </div>
    </section>
  );
}
