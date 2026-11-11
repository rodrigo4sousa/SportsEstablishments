import { useTranslations } from "next-intl";
import { useFormats } from "@/i18n/formats";

type ChartDataTableProps = {
  caption: string;
  /** The header of the first column (the period) and of each series. */
  firstColumn: string;
  series: string[];
  rows: { key: string; label: string; values: number[] }[];
};

/**
 * The numbers behind a chart, as a table anyone can open (UX-027): a screen-reader user gets the data, and so does anyone who
 * cannot tell the series apart or wants an exact figure. A plain `<details>`, so it works with the keyboard and without a script.
 */
export function ChartDataTable({ caption, firstColumn, series, rows }: ChartDataTableProps) {
  const t = useTranslations("admin.chartTable");
  const formats = useFormats();
  return (
    <details className="rounded-md border px-3 py-2 text-sm">
      <summary className="cursor-pointer py-1 font-medium">{t("show")}</summary>
      <div className="mt-2 max-h-72 overflow-y-auto">
        <table className="w-full text-left">
          <caption className="sr-only">{caption}</caption>
          <thead>
            <tr className="border-b">
              <th scope="col" className="py-1 pr-3 font-medium">{firstColumn}</th>
              {series.map((name) => (
                <th key={name} scope="col" className="py-1 text-right font-medium">{name}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.key} className="border-b last:border-b-0">
                <th scope="row" className="py-1 pr-3 font-normal">{row.label}</th>
                {row.values.map((value, index) => (
                  <td key={series[index]} className="py-1 text-right tabular-nums">{formats.count(value)}</td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </details>
  );
}
