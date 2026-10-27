import { describe, expect, it } from 'vitest';
import { changeQuantity, subtotal, validCart, validOrders, products, count } from './catalog';
describe('demo shopping calculations',()=>{
  it('totals quantities using catalog prices',()=>{
    const cart = { avocado: 2, milk: 1 };
    expect(subtotal(cart)).toBe(2 * products.find(p=>p.id==='avocado')!.price + products.find(p=>p.id==='milk')!.price);
    expect(count(cart)).toBe(3);
  });
  it('removes empty lines and bounds quantities',()=>{
    expect(changeQuantity({milk:1},'milk',-1)).toEqual({});
    expect(changeQuantity({milk:20},'milk',1)).toEqual({milk:20});
    expect(changeQuantity({},'unknown',1)).toEqual({});
  });
  it('rejects corrupted saved cart and order data',()=>{
    expect(validCart({milk:-1})).toBe(false);
    expect(validCart({milk:1.5})).toBe(false);
    expect(validCart({missing:2})).toBe(false);
    expect(validOrders([{id:'broken',items:{milk:1}}])).toBe(false);
    expect(validCart({milk:2})).toBe(true);
  });
});
