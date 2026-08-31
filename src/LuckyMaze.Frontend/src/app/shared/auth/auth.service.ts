import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../environments/environment';

interface TokenPair {
  access_token: string;
  refresh_token: string;
  expires_in: number;
  token_type: string;
}

const ACCESS_TOKEN_KEY = 'luckymaze.accessToken';
const REFRESH_TOKEN_KEY = 'luckymaze.refreshToken';

// localStorage can be unavailable (a test environment with no DOM storage polyfill, a restrictive
// browser context) - never let reading or writing a token crash the app over it.
const storage = {
  get(key: string): string | null {
    try {
      return localStorage.getItem(key);
    } catch {
      return null;
    }
  },
  set(key: string, value: string): void {
    try {
      localStorage.setItem(key, value);
    } catch {
      // Best effort - the in-memory signal still carries the token for this page load.
    }
  },
  remove(key: string): void {
    try {
      localStorage.removeItem(key);
    } catch {
      // Nothing to clean up if it never wrote in the first place.
    }
  },
};

function isTokenPair(value: unknown): value is TokenPair {
  return !!value && typeof value === 'object' && 'access_token' in value && 'refresh_token' in value;
}

function decodeJwtPayload(token: string): Record<string, unknown> | null {
  const segment = token.split('.')[1];
  if (!segment) return null;

  try {
    const base64 = segment.replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), '=');
    return JSON.parse(atob(padded)) as Record<string, unknown>;
  } catch {
    return null;
  }
}

/**
 * Talks to Toamaisutaa's local login endpoints (POST /auth/login, /auth/register, /auth/refresh,
 * /auth/logout) and holds the resulting token pair. There is no generated API client for these -
 * they aren't part of the OpenAPI document the backend serves for its own controllers - so this
 * calls them directly.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  private readonly accessTokenSignal = signal<string | null>(storage.get(ACCESS_TOKEN_KEY));
  private refreshTokenValue: string | null = storage.get(REFRESH_TOKEN_KEY);
  private refreshInFlight: Promise<string | null> | null = null;

  readonly isAuthenticated = computed(() => this.accessTokenSignal() !== null);

  readonly roles = computed<string[]>(() => {
    const token = this.accessTokenSignal();
    if (!token) return [];

    const raw = decodeJwtPayload(token)?.['roles'];
    if (Array.isArray(raw)) return raw.filter((role): role is string => typeof role === 'string');
    if (typeof raw === 'string') return [raw];
    return [];
  });

  hasRole(role: string): boolean {
    return this.roles().includes(role);
  }

  getAccessToken(): string | null {
    return this.accessTokenSignal();
  }

  async login(identifier: string, password: string): Promise<void> {
    const response = await firstValueFrom(
      this.http.post<unknown>(`${environment.apiBaseUrl}/auth/login`, { identifier, password }),
    );

    if (!isTokenPair(response)) {
      throw new Error('This account has two-factor authentication enabled, which is not supported here.');
    }

    this.storeTokens(response);
  }

  async register(userName: string, email: string, password: string): Promise<void> {
    const response = await firstValueFrom(
      this.http.post<TokenPair>(`${environment.apiBaseUrl}/auth/register`, { userName, email, password }),
    );

    this.storeTokens(response);
  }

  /** Exchanges the refresh token for a new pair. Concurrent callers share one request. */
  async refresh(): Promise<string | null> {
    if (this.refreshInFlight) return this.refreshInFlight;

    const refreshToken = this.refreshTokenValue;
    if (!refreshToken) return null;

    this.refreshInFlight = (async () => {
      try {
        const response = await firstValueFrom(
          this.http.post<TokenPair>(`${environment.apiBaseUrl}/auth/refresh`, { refreshToken }),
        );
        this.storeTokens(response);
        return response.access_token;
      } catch {
        this.clearTokens();
        return null;
      } finally {
        this.refreshInFlight = null;
      }
    })();

    return this.refreshInFlight;
  }

  async logout(): Promise<void> {
    const refreshToken = this.refreshTokenValue;
    this.clearTokens();

    if (!refreshToken) return;

    try {
      await firstValueFrom(this.http.post(`${environment.apiBaseUrl}/auth/logout`, { refreshToken }));
    } catch {
      // Signed out locally regardless; a failed server-side revoke isn't the caller's problem.
    }
  }

  private storeTokens(tokens: TokenPair): void {
    storage.set(ACCESS_TOKEN_KEY, tokens.access_token);
    storage.set(REFRESH_TOKEN_KEY, tokens.refresh_token);
    this.refreshTokenValue = tokens.refresh_token;
    this.accessTokenSignal.set(tokens.access_token);
  }

  private clearTokens(): void {
    storage.remove(ACCESS_TOKEN_KEY);
    storage.remove(REFRESH_TOKEN_KEY);
    this.refreshTokenValue = null;
    this.accessTokenSignal.set(null);
  }
}

/** Extracts a user-facing message from Toamaisutaa's two error shapes (401 vs 400/409). */
export function extractAuthErrorMessage(err: unknown, fallback: string): string {
  // A plain Error is one this service threw itself (e.g. the two-factor case), not an HTTP failure.
  if (err instanceof Error) return err.message;

  const body = (err as { error?: { error_description?: string; errors?: string[] } })?.error;
  if (body?.errors?.length) return body.errors.join(' ');
  if (body?.error_description) return body.error_description;

  return fallback;
}
