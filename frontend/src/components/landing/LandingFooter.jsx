import React from 'react';

export default function LandingFooter() {
  return (
    <footer className="bg-surface-container-low/50 pt-24 pb-12 px-6 lg:px-16 border-none">
      <div className="max-w-7xl mx-auto grid grid-cols-2 md:grid-cols-4 gap-12 mb-20">
        {/* Brand Column */}
        <div className="col-span-2 md:col-span-1">
          <div className="flex items-center gap-2 mb-6">
            <div className="w-10 h-10 bg-primary rounded-xl flex items-center justify-center text-white shadow-lg shadow-primary/20">
              <span className="material-symbols-outlined font-bold">architecture</span>
            </div>
            <span className="text-2xl font-bold text-on-surface tracking-tight">SperoFlow</span>
          </div>
          <p className="text-sm text-on-surface-variant leading-relaxed mb-8">
            The digital architect's studio for life planning and execution.
          </p>
          <div className="flex gap-4">
            <a className="text-on-surface-variant hover:text-primary transition-colors" href="#">
              <span className="material-symbols-outlined">public</span>
            </a>
            <a className="text-on-surface-variant hover:text-primary transition-colors" href="#">
              <span className="material-symbols-outlined">group</span>
            </a>
            <a className="text-on-surface-variant hover:text-primary transition-colors" href="#">
              <span className="material-symbols-outlined">code</span>
            </a>
          </div>
        </div>

        {/* Product Column */}
        <div>
          <h4 className="font-bold text-on-surface mb-6 uppercase text-xs tracking-widest">Product</h4>
          <ul className="space-y-4 text-sm text-on-surface-variant">
            <li><a className="hover:text-primary transition-colors" href="#">Features</a></li>
            <li><a className="hover:text-primary transition-colors" href="#methodology">Methodology</a></li>
            <li><a className="hover:text-primary transition-colors" href="#modules">Modules</a></li>
            <li><a className="hover:text-primary transition-colors" href="#pricing">Pricing</a></li>
          </ul>
        </div>

        {/* Resources Column */}
        <div>
          <h4 className="font-bold text-on-surface mb-6 uppercase text-xs tracking-widest">Resources</h4>
          <ul className="space-y-4 text-sm text-on-surface-variant">
            <li><a className="hover:text-primary transition-colors" href="#">Blog</a></li>
            <li><a className="hover:text-primary transition-colors" href="#">Help Center</a></li>
            <li><a className="hover:text-primary transition-colors" href="#">Community</a></li>
            <li><a className="hover:text-primary transition-colors" href="#">API Docs</a></li>
          </ul>
        </div>

        {/* Company Column */}
        <div>
          <h4 className="font-bold text-on-surface mb-6 uppercase text-xs tracking-widest">Company</h4>
          <ul className="space-y-4 text-sm text-on-surface-variant">
            <li><a className="hover:text-primary transition-colors" href="#">About</a></li>
            <li><a className="hover:text-primary transition-colors" href="#">Careers</a></li>
            <li><a className="hover:text-primary transition-colors" href="#">Contact</a></li>
            <li><a className="hover:text-primary transition-colors" href="#">Privacy</a></li>
            <li><a className="hover:text-primary transition-colors" href="#">Terms</a></li>
          </ul>
        </div>
      </div>

      {/* Bottom Bar */}
      <div className="max-w-7xl mx-auto pt-8 border-t border-on-surface/5 flex flex-col md:flex-row justify-between items-center gap-4 text-xs text-on-surface-variant/60">
        <p>© 2025 SperoFlow. All rights reserved.</p>
        <p className="font-medium">Mindfully crafted.</p>
      </div>
    </footer>
  );
}
