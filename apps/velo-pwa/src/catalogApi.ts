import type { Product } from './catalog';

type Page<T> = { items: T[]; page: number; pageSize: number; totalCount: number };
export type Category = { id: string; name: string; isActive: boolean };
type CatalogProduct = {
  id: string; categoryId: string; name: string; description: string | null;
  imageUrl: string | null; isActive: boolean;
  variants: { id: string; name: string; price: number; isActive: boolean }[];
};

export async function loadCatalog(signal?: AbortSignal, transport: typeof fetch = fetch): Promise<{ categories: Category[]; products: Product[] }> {
  async function pages<T>(resource: string): Promise<T[]> {
    const items: T[] = [];
    for (let page = 1; ; page++) {
      const response = await transport(`/api/v1/catalog/${resource}?page=${page}&pageSize=100`, {
        signal, cache: 'no-store', credentials: 'omit', redirect: 'error', headers: { Accept: 'application/json' }
      });
      if (!response.ok) throw new Error(`Unable to load the catalog (${response.status}). Please try again.`);
      if (!response.headers.get('Content-Type')?.includes('application/json')) throw new Error('The catalog API returned an unexpected response. Check the API proxy.');
      const data: Page<T> = await response.json();
      if (!Array.isArray(data.items) || data.page !== page || !Number.isInteger(data.totalCount) || data.totalCount < 0) throw new Error('The catalog API returned an invalid page.');
      items.push(...data.items);
      if (items.length >= data.totalCount) return items;
      if (!data.items.length) throw new Error('The catalog changed while loading. Please try again.');
    }
  }
  const [categories, records] = await Promise.all([pages<Category>('categories'), pages<CatalogProduct>('products')]);
  const activeCategories = categories.filter(category => category.isActive);
  const names = new Map(activeCategories.map(category => [category.id, category.name]));
  const products = records.filter(product => product.isActive && names.has(product.categoryId)).flatMap(product =>
    product.variants.filter(variant => variant.isActive).map(variant => ({
      id: variant.id, productId: product.id, name: product.name, detail: variant.name,
      description: product.description ?? '', category: product.categoryId,
      price: variant.price, imageUrl: product.imageUrl, color: '#edf0e9'
    }))
  );
  return { categories: activeCategories, products };
}
