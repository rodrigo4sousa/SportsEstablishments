import Link from "next/link";
import { AccountStatus } from "@/features/auth/components/account-status";

export default function Layout({ children }: { children: React.ReactNode }) {
  return (
    <>
      <header className="border-b">
        <div className="mx-auto flex w-full max-w-3xl items-center justify-between gap-4 px-4 py-3">
          <Link href="/" className="font-semibold tracking-tight">
            Reservas de Campos
          </Link>
          <AccountStatus />
        </div>
      </header>
      <main className="flex flex-1 flex-col">{children}</main>
    </>
  );
}
