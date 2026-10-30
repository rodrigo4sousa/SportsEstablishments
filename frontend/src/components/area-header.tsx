import Link from "next/link";
import { PAGE_WIDTH, type PageWidth } from "@/components/page-width";
import { LogoutButton } from "@/features/auth/components/logout-button";

type AreaHeaderProps = {
  title: string;
  homeHref: string;
  accountName: string;
  /** The width of the area's pages, so the header lines up with them (UX-028). */
  width?: PageWidth;
};

/** Header of the owner and admin areas: where you are, who is signed in, and the way out. */
export function AreaHeader({ title, homeHref, accountName, width = "narrow" }: AreaHeaderProps) {
  return (
    <header className="border-b">
      <div className={`mx-auto flex w-full ${PAGE_WIDTH[width]} items-center justify-between gap-4 px-4 py-3`}>
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
