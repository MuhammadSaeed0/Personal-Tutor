import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiProblem } from './models';

/** Error thrown for any failed API call, carrying the backend's stable error code. */
export class ApiError extends Error {
  constructor(public readonly problem: ApiProblem) {
    super(problem.title ?? problem.code);
  }
  get code(): string { return this.problem.code; }
  get status(): number { return this.problem.status; }
}

export function toApiError(error: unknown): ApiError {
  if (error instanceof ApiError) return error;
  if (error instanceof HttpErrorResponse) {
    const body = error.error;
    if (body && typeof body === 'object' && 'code' in body) return new ApiError({ ...(body as ApiProblem), status: error.status });
    if (error.status === 0) return new ApiError({ status: 0, code: 'network_error' });
    if (error.status === 429) return new ApiError({ status: 429, code: 'rate_limited' });
    return new ApiError({ status: error.status, code: error.status >= 500 ? 'server_error' : 'request_failed' });
  }
  return new ApiError({ status: 0, code: 'unknown_error' });
}

type Query = Record<string, string | number | boolean | null | undefined | readonly (string | number)[]>;

/** Thin promise-based wrapper over HttpClient with consistent error mapping. */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  get<T>(url: string, query?: Query): Promise<T> {
    return this.run(this.http.get<T>(url, { params: toParams(query) }));
  }

  post<T>(url: string, body?: unknown, query?: Query): Promise<T> {
    return this.run(this.http.post<T>(url, body ?? {}, { params: toParams(query) }));
  }

  put<T>(url: string, body?: unknown): Promise<T> {
    return this.run(this.http.put<T>(url, body ?? {}));
  }

  patch<T>(url: string, body?: unknown): Promise<T> {
    return this.run(this.http.patch<T>(url, body ?? {}));
  }

  delete<T = void>(url: string): Promise<T> {
    return this.run(this.http.delete<T>(url));
  }

  blob(url: string, query?: Query): Promise<Blob> {
    return this.run(this.http.get(url, { params: toParams(query), responseType: 'blob' }));
  }

  private async run<T>(request: import('rxjs').Observable<T>): Promise<T> {
    try {
      return await firstValueFrom(request);
    } catch (error) {
      throw toApiError(error);
    }
  }
}

function toParams(query?: Query): HttpParams | undefined {
  if (!query) return undefined;
  let params = new HttpParams();
  for (const [key, value] of Object.entries(query)) {
    if (value === null || value === undefined || value === '') continue;
    if (Array.isArray(value)) value.forEach(v => (params = params.append(key, String(v))));
    else params = params.set(key, String(value));
  }
  return params;
}

/** Saves a Blob as a file download. */
export function saveBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = fileName;
  a.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
