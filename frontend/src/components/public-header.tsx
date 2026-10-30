import Link from "next/link";
import { PAGE_WIDTH } from "@/components/page-width";
import { AccountStatus } from "@/features/auth/components/account-status";

type PublicHeaderProps = {
  /** Show who is signed in, or the sign-in and sign-up links. The account pages leave it out: they are those links' destination. */
  account?: boolean;
};

/** The header of the player's pages, the account pages and the error and not-found pages: the brand, and the account links. */
export function PublicHeader({ account = true }: PublicHeaderProps) {
  return (
    <header className="border-b">
      <div className={`mx-auto flex w-full ${PAGE_WIDTH.narrow} items-center justify-between gap-4 px-4 py-3`}>
        <Link href="/" className="font-semibold tracking-tight">
          Reservas de Campos
        </Link>
        {account ? <AccountStatus /> : null}
      </div>
    </header>
  );
}
