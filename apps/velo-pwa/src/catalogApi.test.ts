import { describe, expect, it, vi } from 'vitest';
import { loadCatalog } from './catalogApi';

const json = (items: unknown[], totalCount = items.length, page = 1) => new Response(JSON.stringify({items,totalCount,page,pageSize:100}), {headers:{'Content-Type':'application/json'}});
const category = {id:'category',name:'Fresh food',isActive:true};
const product = {id:'product',categoryId:'category',name:'Milk',description:'Fresh milk',imageUrl:'https://example.com/milk.jpg',isActive:true,variants:[
  {id:'small',name:'500 ml',price:25,isActive:true},
  {id:'large',name:'1 litre',price:45,isActive:true},
  {id:'hidden',name:'Old pack',price:30,isActive:false}
]};

describe('public catalog',()=>{
  it('loads every page and keeps each active SKU and API image and price',async()=>{
    const transport=vi.fn(async (url: RequestInfo | URL)=>String(url).includes('/categories?')?json([category]):String(url).includes('page=1&')?json([product],2):json([{...product,id:'second',variants:[]}],2,2));
    const signal=new AbortController().signal;
    const result=await loadCatalog(signal,transport as typeof fetch);
    expect(result.categories).toEqual([category]);
    expect(result.products.map(p=>[p.id,p.detail,p.price])).toEqual([['small','500 ml',25],['large','1 litre',45]]);
    expect(result.products[0]).toMatchObject({productId:'product',imageUrl:product.imageUrl,description:'Fresh milk'});
    expect(transport).toHaveBeenCalledWith('/api/v1/catalog/products?page=2&pageSize=100',expect.objectContaining({signal,cache:'no-store',credentials:'omit'}));
  });
  it('returns an empty catalog without sample products',async()=>{
    expect(await loadCatalog(undefined,vi.fn(async()=>json([])) as typeof fetch)).toEqual({categories:[],products:[]});
  });
  it('omits inactive products and categories',async()=>{
    const transport=vi.fn(async(url: RequestInfo | URL)=>String(url).includes('/categories?')?json([category,{...category,id:'hidden-category',isActive:false}]):json([
      {...product,isActive:false},{...product,id:'hidden-parent',categoryId:'hidden-category'}
    ]));
    expect((await loadCatalog(undefined,transport as typeof fetch)).products).toEqual([]);
  });
  it('rejects incomplete pagination instead of silently dropping products',async()=>{
    const transport=vi.fn(async(url: RequestInfo | URL)=>String(url).includes('/categories?')?json([category]):String(url).includes('page=1&')?json([product],2):json([],2,2));
    await expect(loadCatalog(undefined,transport as typeof fetch)).rejects.toThrow('changed while loading');
  });
  it.each([503,404])('surfaces API failure %s',async status=>{
    await expect(loadCatalog(undefined,vi.fn(async()=>new Response('',{status})) as typeof fetch)).rejects.toThrow(String(status));
  });
  it('rejects an HTML proxy fallback',async()=>{
    await expect(loadCatalog(undefined,vi.fn(async()=>new Response('<html/>',{headers:{'Content-Type':'text/html'}})) as typeof fetch)).rejects.toThrow('proxy');
  });
  it('propagates cancellation',async()=>{
    const controller=new AbortController();controller.abort();
    await expect(loadCatalog(controller.signal,vi.fn(async()=>{throw new DOMException('Aborted','AbortError');}) as typeof fetch)).rejects.toMatchObject({name:'AbortError'});
  });
});
