import Link from "next/link";
import { PAGE_WIDTH, type PageWidth } from "@/components/page-width";
import { AccountMenu, type AccountMenuLink } from "@/components/account-menu";

type AreaHeaderProps = {
  title: string;
  homeHref: string;
  accountName: string;
  /** The width of the area's pages, so the header lines up with them (UX-028). */
  width?: PageWidth;
  /** The area's main pages, in the account menu (UX-013). */
  links?: AccountMenuLink[];
};

/** Header of the owner and admin areas: where you are, who is signed in, the area's pages and the way out (the same menu as the player header). */
export function AreaHeader({ title, homeHref, accountName, width = "narrow", links }: AreaHeaderProps) {
  return (
    <header className="border-b">
      <div className={`mx-auto flex w-full ${PAGE_WIDTH[width]} items-center justify-between gap-4 px-4 py-3`}>
        <Link href={homeHref} className="font-semibold tracking-tight">
          {title}
        </Link>
        <AccountMenu accountName={accountName} links={links} />
      </div>
    </header>
  );
}
