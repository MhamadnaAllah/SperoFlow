"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";

export default function LandingNavbar() {
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
  const router = useRouter();

  const handleNavigation = (path) => {
    router.push(path);
    setMobileMenuOpen(false);
  };

  return (
    <nav className="fixed top-0 w-full z-50 bg-white/80 backdrop-blur-xl border-none shadow-sm flex items-center justify-between px-6 h-16 transition-all duration-200">
      <div className="flex items-center gap-8">
        <span className="text-2xl font-bold tracking-tight text-on-surface">SperoFlow</span>
        <div className="hidden md:flex gap-6">
          <a className="text-on-surface-variant hover:text-on-surface transition-colors text-sm font-medium" href="#methodology">Methodology</a>
          <a className="text-on-surface-variant hover:text-on-surface transition-colors text-sm font-medium" href="#modules">Modules</a>
          <a className="text-on-surface-variant hover:text-on-surface transition-colors text-sm font-medium" href="#pricing">Pricing</a>
        </div>
      </div>
      
      <div className="hidden md:flex items-center gap-4">
        <button 
          onClick={() => handleNavigation('/auth')}
          className="text-on-surface-variant hover:text-primary transition-colors text-sm font-medium flex items-center gap-2"
        >
          <span className="material-symbols-outlined text-[20px]">login</span> Sign In
        </button>
        <button 
          onClick={() => handleNavigation('/auth')}
          className="bg-primary text-white text-sm font-medium px-4 py-2 rounded-full hover:bg-primary/90 transition-all flex items-center gap-2"
        >
          <span className="material-symbols-outlined text-[20px]">person_add</span> Register
        </button>
      </div>

      {/* Mobile Menu Toggle */}
      <button 
        className="md:hidden text-on-surface-variant hover:text-on-surface"
        onClick={() => setMobileMenuOpen(!mobileMenuOpen)}
      >
        <span className="material-symbols-outlined text-2xl">
          {mobileMenuOpen ? 'close' : 'menu'}
        </span>
      </button>

      {/* Mobile Nav Panel */}
      {mobileMenuOpen && (
        <div className="absolute top-16 left-0 w-full bg-white/95 backdrop-blur-xl shadow-md border-t border-surface-container-highest md:hidden flex flex-col px-6 py-4 gap-4 mobile-nav-panel">
          <a className="text-on-surface-variant hover:text-on-surface transition-colors text-base font-medium py-2" href="#methodology" onClick={() => setMobileMenuOpen(false)}>Methodology</a>
          <a className="text-on-surface-variant hover:text-on-surface transition-colors text-base font-medium py-2" href="#modules" onClick={() => setMobileMenuOpen(false)}>Modules</a>
          <a className="text-on-surface-variant hover:text-on-surface transition-colors text-base font-medium py-2" href="#pricing" onClick={() => setMobileMenuOpen(false)}>Pricing</a>
          
          <div className="h-px bg-surface-container-highest w-full my-2"></div>
          
          <button 
            onClick={() => handleNavigation('/auth')}
            className="text-on-surface-variant hover:text-primary transition-colors text-base font-medium flex items-center gap-2 py-2"
          >
            <span className="material-symbols-outlined text-[20px]">login</span> Sign In
          </button>
          <button 
            onClick={() => handleNavigation('/auth')}
            className="bg-primary text-white text-base font-medium px-4 py-2 rounded-full hover:bg-primary/90 transition-all flex items-center justify-center gap-2 mt-2 w-full"
          >
            <span className="material-symbols-outlined text-[20px]">person_add</span> Register
          </button>
        </div>
      )}
    </nav>
  );
}
