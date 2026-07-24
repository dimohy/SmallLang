import type { Metadata } from "next";
import "./globals.css";

const basePath = process.env.NEXT_PUBLIC_BASE_PATH ?? "";
const publicUrl = "https://dimohy.github.io/sollang/";

export const metadata: Metadata = {
  title: "Sollang Playground",
  description: "Write, compile, and run Sollang directly in your browser.",
  metadataBase: new URL(publicUrl),
  icons: {
    icon: `${basePath}/sollang-logo.svg`
  },
  openGraph: {
    title: "Sollang Playground",
    description: "A flow-first language, running entirely in your browser.",
    url: publicUrl,
    siteName: "Sollang",
    type: "website"
  }
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
