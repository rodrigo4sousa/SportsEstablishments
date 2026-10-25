import { Suspense } from "react";
import { AdminDashboard } from "@/features/admin/components/admin-dashboard";
import { loadAdminStats } from "@/lib/server/admin-stats";

/** US24, US25, US26: the first load reads the totals on the server; the chart and the table load in the browser. */
export default function Page() {
  return (
    <section className="mx-auto flex w-full max-w-6xl flex-1 flex-col gap-6 px-4 py-10">
      <h1 className="text-2xl font-semibold tracking-tight">Administração</h1>
      <Suspense
        fallback={
          <p role="status" className="text-muted-foreground">
            A carregar…
          </p>
        }
      >
        <Dashboard />
      </Suspense>
    </section>
  );
}

/** Reads the request's cookies, so it renders inside the page's `<Suspense>` (Cache Components). */
async function Dashboard() {
  return <AdminDashboard initialStats={await loadAdminStats()} />;
}
