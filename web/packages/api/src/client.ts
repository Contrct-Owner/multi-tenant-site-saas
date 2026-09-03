// The typed client over the generated OpenAPI types (ADR 16). Paths are the
// contract's path templates, path and query parameters and request bodies are
// the operation's, and the response is the operation's 2xx JSON - so a
// contract change fails the typecheck at every affected call instead of at
// runtime. Session rides the HttpOnly cookie (ADR 21): credentials always
// included, no tokens in JS.
//
// Endpoints the API has not typed yet (the typed-response ratchet) describe
// their body as an empty object; for those the caller's generic stands in,
// exactly as before. For a typed endpoint the caller's generic is ignored and
// the contract's shape is what comes back.
import type { paths } from './types';

export type ApiPaths = paths;

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly body: unknown,
  ) {
    super(`API ${status}`);
  }
}

type Method = 'get' | 'post' | 'put' | 'delete';

/** Paths that offer the method. */
type PathsFor<M extends Method> = {
  [P in keyof paths]: paths[P][M] extends undefined | never ? never : P;
}[keyof paths];

type Op<P extends keyof paths, M extends Method> = NonNullable<paths[P][M]>;

type Params<O> = O extends { parameters: infer Pa } ? Pa : never;
type PathParams<O> = Params<O> extends { path: infer T } ? T : undefined;
type QueryParams<O> = Params<O> extends { query?: infer T } ? T : undefined;
type RequestBody<O> = O extends { requestBody: { content: { 'application/json': infer B } } }
  ? B
  : O extends { requestBody?: { content: { 'application/json': infer B } } }
    ? B | undefined
    : undefined;

type Success<R> = R extends { 200: infer A }
  ? A
  : R extends { 201: infer A }
    ? A
    : R extends { 202: infer A }
      ? A
      : never;
type Json<C> = C extends { content: { 'application/json': infer J } } ? J : unknown;
type Response<O> = O extends { responses: infer R } ? Json<Success<R>> : unknown;
/** An untyped endpoint (empty-object body) hands the caller's generic back; a typed one wins. */
type Result<O, T> = [Response<O>] extends [never]
  ? T
  : Response<O> extends Record<string, never>
    ? T
    : Response<O>;

/** Options an operation accepts: `path` when the template has parameters, `query` when it takes any. */
type Init<O> = (PathParams<O> extends undefined ? { path?: undefined } : { path: PathParams<O> }) &
  (QueryParams<O> extends undefined ? { query?: undefined } : { query?: QueryParams<O> });

// Vite injects env at build; plain node (Start server) falls back cleanly
const base: string =
  (import.meta as { env?: { VITE_API_URL?: string } }).env?.VITE_API_URL ?? '';

function url(template: string, init?: { path?: unknown; query?: unknown }): string {
  const filled = template.replace(/\{(\w+)\}/g, (_, key: string) => {
    const value = (init?.path as Record<string, string | number> | undefined)?.[key];
    if (value === undefined) throw new Error(`missing path parameter '${key}' for ${template}`);
    return encodeURIComponent(String(value));
  });
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries((init?.query as Record<string, unknown>) ?? {}))
    if (value !== undefined && value !== null) search.set(key, String(value));
  const qs = search.toString();
  return `${base}${filled}${qs ? `?${qs}` : ''}`;
}

async function request<T>(
  method: string,
  template: string,
  init?: { path?: unknown; query?: unknown },
  body?: unknown,
): Promise<T> {
  const response = await fetch(url(template, init), {
    method,
    credentials: 'include',
    headers: body === undefined ? {} : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await response.text();
  const parsed: unknown = text.length > 0 ? JSON.parse(text) : undefined;
  if (!response.ok) throw new ApiError(response.status, parsed);
  return parsed as T;
}

export const api = {
  get: <P extends PathsFor<'get'>, T = unknown>(path: P, init?: Init<Op<P, 'get'>>) =>
    request<Result<Op<P, 'get'>, T>>('GET', path, init),
  post: <P extends PathsFor<'post'>, T = unknown>(
    path: P,
    body?: RequestBody<Op<P, 'post'>>,
    init?: Init<Op<P, 'post'>>,
  ) => request<Result<Op<P, 'post'>, T>>('POST', path, init, body),
  put: <P extends PathsFor<'put'>, T = unknown>(
    path: P,
    body?: RequestBody<Op<P, 'put'>>,
    init?: Init<Op<P, 'put'>>,
  ) => request<Result<Op<P, 'put'>, T>>('PUT', path, init, body),
  del: <P extends PathsFor<'delete'>, T = unknown>(path: P, init?: Init<Op<P, 'delete'>>) =>
    request<Result<Op<P, 'delete'>, T>>('DELETE', path, init),
};
