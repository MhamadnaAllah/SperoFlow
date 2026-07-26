import "./globals.css";

export const metadata = {
  title: "Knowledge Portal",
  description: "Private SperoFlow knowledge administration.",
};

export default function RootLayout({ children }) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
