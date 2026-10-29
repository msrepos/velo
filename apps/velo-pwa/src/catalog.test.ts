import { describe, expect, it } from 'vitest';
import { changeQuantity, subtotal, validCart, validOrders, count, type Product } from './catalog';
const products: Product[] = [
  {id:'avocado',productId:'p1',name:'Avocado',detail:'2 pieces',description:'',category:'fruit',price:89,imageUrl:null,color:'#fff'},
  {id:'milk',productId:'p2',name:'Milk',detail:'1 litre',description:'',category:'dairy',price:52,imageUrl:null,color:'#fff'}
];
describe('shopping calculations',()=>{
  it('totals quantities using catalog prices',()=>{
    const cart = { avocado: 2, milk: 1 };
    expect(subtotal(cart,products)).toBe(2 * products.find(p=>p.id==='avocado')!.price + products.find(p=>p.id==='milk')!.price);
    expect(count(cart)).toBe(3);
  });
  it('removes empty lines and bounds quantities',()=>{
    expect(changeQuantity({milk:1},'milk',-1,products)).toEqual({});
    expect(changeQuantity({milk:20},'milk',1,products)).toEqual({milk:20});
    expect(changeQuantity({},'unknown',1,products)).toEqual({});
  });
  it('rejects corrupted saved cart and order data',()=>{
    expect(validCart({milk:-1})).toBe(false);
    expect(validCart({milk:1.5})).toBe(false);
    expect(validCart({missing:2})).toBe(true); // API membership is checked after loading.
    expect(subtotal({missing:2},products)).toBe(0);
    expect(validOrders([{id:'broken',items:{milk:1}}])).toBe(false);
    expect(validCart({milk:2})).toBe(true);
  });
});
