import Link from "next/link";
import { LogoutButton } from "@/features/auth/components/logout-button";

type AreaHeaderProps = {
  title: string;
  homeHref: string;
  accountName: string;
};

/** Header of the owner and admin areas: where you are, who is signed in, and the way out. */
export function AreaHeader({ title, homeHref, accountName }: AreaHeaderProps) {
  return (
    <header className="border-b">
      <div className="mx-auto flex w-full max-w-3xl items-center justify-between gap-4 px-4 py-3">
        <Link href={homeHref} className="font-semibold tracking-tight">
          {title}
        </Link>
        <div className="flex items-center gap-3">
          <p className="text-sm">
            Olá, <span className="font-medium">{accountName}</span>
          </p>
          <LogoutButton />
        </div>
      </div>
    </header>
  );
}
