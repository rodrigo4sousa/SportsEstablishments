import { OwnerNotFound } from "@/features/admin/components/owner-details";

export default function NotFound() {
  return (
    <section className="mx-auto flex w-full max-w-4xl flex-1 flex-col gap-6 px-4 py-10">
      <h1 className="text-2xl font-semibold tracking-tight">Dono</h1>
      <OwnerNotFound />
    </section>
  );
}
