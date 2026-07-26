import './globals.css';

export const metadata = {
  title: 'SperoFlow | AI-Powered Life Dashboard',
  description:
    'SperoFlow – Your unified AI-powered dashboard for goals, habits, tasks, and personal growth management.',
};

export default function RootLayout({ children }) {
  return (
    <html lang="en" className="light">
      <head>
        <link rel="preconnect" href="https://fonts.googleapis.com" />
        <link rel="preconnect" href="https://fonts.gstatic.com" crossOrigin="anonymous" />
        <link
          href="https://fonts.googleapis.com/css2?family=Inter:wght@300;400;500;600;700;800;900&family=Source+Serif+4:ital,opsz,wght@0,8..60,400;1,8..60,400&family=Hanken+Grotesk:wght@400;500;600;700;800&family=JetBrains+Mono:wght@400;500;600&display=swap"
          rel="stylesheet"
        />
        <link
          href="https://fonts.googleapis.com/css2?family=Material+Symbols+Outlined:wght,FILL@100..700,0..1&display=swap"
          rel="stylesheet"
        />
      </head>
      <body className="bg-surface font-body text-on-surface antialiased overflow-hidden h-screen flex flex-col">
        {children}
      </body>
    </html>
  );
}
