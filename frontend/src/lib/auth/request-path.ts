/**
 * Name of the request header in which `proxy.ts` puts the path (and query) of the page being requested, so that
 * server components, which do not know their own URL, can send a visitor to `/login?next=<this page>`.
 */
export const REQUEST_PATH_HEADER = "x-pathname";
