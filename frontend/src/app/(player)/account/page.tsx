import type { Metadata } from "next";
import { PageShell } from "@/components/page-shell";
import { PrivacySection } from "@/features/account/components/privacy-section";

export const metadata: Metadata = {
  title: "A minha conta",
  robots: { index: false },
};

export default function Page() {
  return (
    <PageShell title="A minha conta">
      <PrivacySection />
    </PageShell>
  );
}
