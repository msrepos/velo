import { describe, expect, it, vi } from 'vitest';
import { createApi } from './api';
import { allPages, availableTeam, type Dispatch, type Minivan, type Person } from './fleetApi';

const person = (id: string, userId = id, isActive = true): Person => ({ id, userId, name: id, isActive, version: '' });
const van = (id: string, isActive = true): Minivan => ({ id, name: id, code: id, address: null, latitude: null, longitude: null, isActive, version: '' });
const shift = (driverId: string, crewMemberId: string, warehouseId: string, endedAt: string | null = null) => ({ driverId, crewMemberId, warehouseId, endedAt } as Dispatch);

describe('dispatch availability', () => {
  it('excludes occupied vans and people, including the same account registered in both roles', () => {
    const result = availableTeam([person('d1', 'shared'), person('d2'), person('d3', 'u3', false)],
      [person('c1'), person('c2', 'shared'), person('c3')], [van('v1'), van('v2'), van('v3', false)], [shift('d1', 'c1', 'v1')]);
    expect(result.drivers.map(p => p.id)).toEqual(['d2']);
    expect(result.crew.map(p => p.id)).toEqual(['c3']);
    expect(result.vans.map(v => v.id)).toEqual(['v2']);
  });
  it('releases resources after a shift ends', () => {
    const result = availableTeam([person('d')], [person('c')], [van('v')], [shift('d', 'c', 'v', '2026-10-29')]);
    expect(result.drivers).toHaveLength(1); expect(result.crew).toHaveLength(1); expect(result.vans).toHaveLength(1);
  });
});

describe('complete fleet selectors', () => {
  it('follows pagination while preserving filters and the authenticated no-store client', async () => {
    const transport = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify({ items: [{ id: 1 }], totalCount: 2 }), { headers: { 'Content-Type': 'application/json' } }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ items: [{ id: 2 }], totalCount: 2 }), { headers: { 'Content-Type': 'application/json' } }));
    const result = await allPages(createApi(() => 'token', vi.fn(), transport), '/admin/delivery/dispatches?activeOnly=true');
    expect(result).toEqual([{ id: 1 }, { id: 2 }]);
    expect(transport.mock.calls[1][0]).toContain('activeOnly=true&page=2&pageSize=100');
    expect(transport.mock.calls[1][1]).toMatchObject({ cache: 'no-store', headers: { Authorization: 'Bearer token' } });
  });
  it('fails on an incomplete resource list instead of showing false availability', async () => {
    const transport = vi.fn().mockResolvedValue(new Response(JSON.stringify({ items: [], totalCount: 3 }), { headers: { 'Content-Type': 'application/json' } }));
    await expect(allPages(createApi(() => undefined, vi.fn(), transport), '/fleet')).rejects.toThrow('resource list changed');
  });
});
