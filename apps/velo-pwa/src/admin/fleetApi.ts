import type { createApi, Page } from './api';

export type Api = ReturnType<typeof createApi>;
export type Person = { id: string; userId: string; name: string; isActive: boolean; version: string };
export type Minivan = { id: string; code: string; name: string; address: string | null; latitude: number | null; longitude: number | null; isActive: boolean; version: string };
export type Dispatch = { id: string; warehouseId: string; vanCode: string; vanName: string; driverId: string; driverName: string; crewMemberId: string; crewMemberName: string; startedAt: string; endedAt: string | null; version: string };

// Fetch every page for selectors: never silently omit resources after the first 100.
export async function allPages<T>(api: Api, path: string, signal?: AbortSignal): Promise<T[]> {
  const rows: T[] = [];
  for (let page = 1; ; page++) {
    const result = await api<Page<T>>(`${path}${path.includes('?') ? '&' : '?'}page=${page}&pageSize=100`, 'GET', undefined, undefined, signal);
    rows.push(...result.items);
    if (rows.length >= result.totalCount) return rows;
    if (!result.items.length) throw new Error('The resource list changed. Refresh and try again.');
  }
}

export function availableTeam(drivers: Person[], crew: Person[], vans: Minivan[], dispatches: Dispatch[]) {
  const active = dispatches.filter(d => !d.endedAt);
  const occupiedUsers = new Set(active.flatMap(d => [drivers.find(p => p.id === d.driverId)?.userId, crew.find(p => p.id === d.crewMemberId)?.userId]));
  return {
    drivers: drivers.filter(p => p.isActive && !occupiedUsers.has(p.userId) && !active.some(d => d.driverId === p.id)),
    crew: crew.filter(p => p.isActive && !occupiedUsers.has(p.userId) && !active.some(d => d.crewMemberId === p.id)),
    vans: vans.filter(v => v.isActive && !active.some(d => d.warehouseId === v.id))
  };
}
