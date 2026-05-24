export interface DBSchema {
  [storeName: string]: {
    key: IDBValidKey;
    value: unknown;
    indexes?: Record<string, IDBValidKey>;
  };
}

type StoreName<TSchema extends DBSchema> = Extract<keyof TSchema, string>;

type StoreValue<TSchema extends DBSchema, TStore extends StoreName<TSchema>> =
  TSchema[TStore] extends { value: infer TValue } ? TValue : never;

type StoreKey<TSchema extends DBSchema, TStore extends StoreName<TSchema>> =
  TSchema[TStore] extends { key: infer TKey }
    ? TKey extends IDBValidKey
      ? TKey
      : IDBValidKey
    : IDBValidKey;

type StoreIndexes<TSchema extends DBSchema, TStore extends StoreName<TSchema>> =
  TSchema[TStore] extends { indexes: infer TIndexes }
    ? TIndexes extends Record<string, IDBValidKey>
      ? TIndexes
      : Record<string, IDBValidKey>
    : Record<string, IDBValidKey>;

type StoreIndexName<TSchema extends DBSchema, TStore extends StoreName<TSchema>> = Extract<
  keyof StoreIndexes<TSchema, TStore>,
  string
>;

type StoreIndexKey<
  TSchema extends DBSchema,
  TStore extends StoreName<TSchema>,
  TIndex extends StoreIndexName<TSchema, TStore>,
> = StoreIndexes<TSchema, TStore>[TIndex];

function requestToPromise<T>(request: IDBRequest<T>): Promise<T> {
  return new Promise((resolve, reject) => {
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error ?? new Error('IndexedDB request failed'));
  });
}

function transactionDone(transaction: IDBTransaction): Promise<void> {
  return new Promise((resolve, reject) => {
    transaction.addEventListener('complete', () => resolve(), { once: true });
    transaction.addEventListener(
      'error',
      () => reject(transaction.error ?? new Error('IndexedDB transaction failed')),
      { once: true },
    );
    transaction.addEventListener(
      'abort',
      () => reject(transaction.error ?? new Error('IndexedDB transaction aborted')),
      { once: true },
    );
  });
}

export interface IDBPCursorWithValue<TValue> {
  readonly value: TValue;
  delete(): Promise<void>;
  update(value: TValue): Promise<void>;
  continue(): Promise<IDBPCursorWithValue<TValue> | null>;
}

class CursorWithValue<TValue> implements IDBPCursorWithValue<TValue> {
  constructor(private readonly cursor: IDBCursorWithValue) {}

  get value(): TValue {
    return this.cursor.value as TValue;
  }

  async delete(): Promise<void> {
    await requestToPromise(this.cursor.delete());
  }

  async update(value: TValue): Promise<void> {
    await requestToPromise(this.cursor.update(value));
  }

  async continue(): Promise<IDBPCursorWithValue<TValue> | null> {
    const request = this.cursor.request as IDBRequest<IDBCursorWithValue | null>;
    this.cursor.continue();
    const nextCursor = await requestToPromise(request);
    return nextCursor ? new CursorWithValue<TValue>(nextCursor) : null;
  }
}

class IDBPIndex<TValue> {
  constructor(private readonly index: IDBIndex) {}

  async openCursor(
    query: IDBValidKey | IDBKeyRange | null = null,
    direction?: IDBCursorDirection,
  ): Promise<IDBPCursorWithValue<TValue> | null> {
    const request = direction ? this.index.openCursor(query, direction) : this.index.openCursor(query);
    const cursor = await requestToPromise(request);
    return cursor ? new CursorWithValue<TValue>(cursor) : null;
  }
}

class IDBPObjectStore<TValue> {
  constructor(private readonly store: IDBObjectStore) {}

  get indexNames(): DOMStringList {
    return this.store.indexNames;
  }

  createIndex(name: string, keyPath: string | string[], options?: IDBIndexParameters): IDBIndex {
    return this.store.createIndex(name, keyPath, options);
  }

  deleteIndex(name: string): void {
    this.store.deleteIndex(name);
  }

  async get<TKey extends IDBValidKey>(key: TKey): Promise<TValue | undefined> {
    return (await requestToPromise(this.store.get(key))) as TValue | undefined;
  }

  async put<TKey extends IDBValidKey>(value: TValue, key?: TKey): Promise<void> {
    const request = key === undefined ? this.store.put(value) : this.store.put(value, key);
    await requestToPromise(request);
  }

  async add<TKey extends IDBValidKey>(value: TValue, key?: TKey): Promise<void> {
    const request = key === undefined ? this.store.add(value) : this.store.add(value, key);
    await requestToPromise(request);
  }

  async delete<TKey extends IDBValidKey>(key: TKey): Promise<void> {
    await requestToPromise(this.store.delete(key));
  }

  async clear(): Promise<void> {
    await requestToPromise(this.store.clear());
  }

  async getAll(): Promise<TValue[]> {
    return (await requestToPromise(this.store.getAll())) as TValue[];
  }

  async getAllKeys<TKey extends IDBValidKey>(): Promise<TKey[]> {
    return (await requestToPromise(this.store.getAllKeys())) as TKey[];
  }

  async count(): Promise<number> {
    return await requestToPromise(this.store.count());
  }

  index(name: string): IDBPIndex<TValue> {
    return new IDBPIndex<TValue>(this.store.index(name));
  }

  async openCursor(
    query: IDBValidKey | IDBKeyRange | null = null,
    direction?: IDBCursorDirection,
  ): Promise<IDBPCursorWithValue<TValue> | null> {
    const request = direction ? this.store.openCursor(query, direction) : this.store.openCursor(query);
    const cursor = await requestToPromise(request);
    return cursor ? new CursorWithValue<TValue>(cursor) : null;
  }
}

class IDBPTransaction<TSchema extends DBSchema, TStore extends StoreName<TSchema>> {
  public readonly store: IDBPObjectStore<StoreValue<TSchema, TStore>>;
  public readonly done: Promise<void>;

  constructor(
    private readonly transaction: IDBTransaction,
    storeName: TStore,
  ) {
    this.store = new IDBPObjectStore<StoreValue<TSchema, TStore>>(this.transaction.objectStore(storeName));
    this.done = transactionDone(this.transaction);
  }

  objectStore<TRequestedStore extends StoreName<TSchema>>(
    storeName: TRequestedStore,
  ): IDBPObjectStore<StoreValue<TSchema, TRequestedStore>> {
    return new IDBPObjectStore<StoreValue<TSchema, TRequestedStore>>(this.transaction.objectStore(storeName));
  }
}

export class IDBPDatabase<TSchema extends DBSchema> {
  constructor(private readonly database: IDBDatabase) {}

  get name(): string {
    return this.database.name;
  }

  get version(): number {
    return this.database.version;
  }

  async get<TStore extends StoreName<TSchema>>(
    storeName: TStore,
    key: StoreKey<TSchema, TStore>,
  ): Promise<StoreValue<TSchema, TStore> | undefined> {
    const transaction = this.database.transaction(storeName, 'readonly');
    const result = (await requestToPromise(
      transaction.objectStore(storeName).get(key),
    )) as StoreValue<TSchema, TStore> | undefined;
    await transactionDone(transaction);
    return result;
  }

  async put<TStore extends StoreName<TSchema>>(
    storeName: TStore,
    value: StoreValue<TSchema, TStore>,
    key?: StoreKey<TSchema, TStore>,
  ): Promise<void> {
    const transaction = this.database.transaction(storeName, 'readwrite');
    const store = transaction.objectStore(storeName);
    const request = key === undefined ? store.put(value) : store.put(value, key);
    await requestToPromise(request);
    await transactionDone(transaction);
  }

  async add<TStore extends StoreName<TSchema>>(
    storeName: TStore,
    value: StoreValue<TSchema, TStore>,
    key?: StoreKey<TSchema, TStore>,
  ): Promise<void> {
    const transaction = this.database.transaction(storeName, 'readwrite');
    const store = transaction.objectStore(storeName);
    const request = key === undefined ? store.add(value) : store.add(value, key);
    await requestToPromise(request);
    await transactionDone(transaction);
  }

  async delete<TStore extends StoreName<TSchema>>(
    storeName: TStore,
    key: StoreKey<TSchema, TStore>,
  ): Promise<void> {
    const transaction = this.database.transaction(storeName, 'readwrite');
    await requestToPromise(transaction.objectStore(storeName).delete(key));
    await transactionDone(transaction);
  }

  async clear<TStore extends StoreName<TSchema>>(storeName: TStore): Promise<void> {
    const transaction = this.database.transaction(storeName, 'readwrite');
    await requestToPromise(transaction.objectStore(storeName).clear());
    await transactionDone(transaction);
  }

  async getAll<TStore extends StoreName<TSchema>>(storeName: TStore): Promise<StoreValue<TSchema, TStore>[]> {
    const transaction = this.database.transaction(storeName, 'readonly');
    const result = (await requestToPromise(
      transaction.objectStore(storeName).getAll(),
    )) as StoreValue<TSchema, TStore>[];
    await transactionDone(transaction);
    return result;
  }

  async getAllKeys<TStore extends StoreName<TSchema>>(storeName: TStore): Promise<StoreKey<TSchema, TStore>[]> {
    const transaction = this.database.transaction(storeName, 'readonly');
    const result = (await requestToPromise(
      transaction.objectStore(storeName).getAllKeys(),
    )) as StoreKey<TSchema, TStore>[];
    await transactionDone(transaction);
    return result;
  }

  async getAllFromIndex<
    TStore extends StoreName<TSchema>,
    TIndex extends StoreIndexName<TSchema, TStore>,
  >(
    storeName: TStore,
    indexName: TIndex,
    query: StoreIndexKey<TSchema, TStore, TIndex>,
  ): Promise<StoreValue<TSchema, TStore>[]> {
    const transaction = this.database.transaction(storeName, 'readonly');
    const result = (await requestToPromise(
      transaction.objectStore(storeName).index(indexName).getAll(query),
    )) as StoreValue<TSchema, TStore>[];
    await transactionDone(transaction);
    return result;
  }

  async count<TStore extends StoreName<TSchema>>(storeName: TStore): Promise<number> {
    const transaction = this.database.transaction(storeName, 'readonly');
    const result = await requestToPromise(transaction.objectStore(storeName).count());
    await transactionDone(transaction);
    return result;
  }

  transaction<TStore extends StoreName<TSchema>>(
    storeName: TStore,
    mode: IDBTransactionMode,
  ): IDBPTransaction<TSchema, TStore> {
    return new IDBPTransaction<TSchema, TStore>(this.database.transaction(storeName, mode), storeName);
  }
}

interface OpenDBOptions {
  upgrade?: (
    database: IDBDatabase,
    oldVersion: number,
    newVersion: number | null,
    transaction: IDBTransaction,
  ) => void;
}

export function openDB<TSchema extends DBSchema>(
  name: string,
  version: number,
  options?: OpenDBOptions,
): Promise<IDBPDatabase<TSchema>> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(name, version);

    request.onupgradeneeded = event => {
      if (!options?.upgrade) return;
      const transaction = request.transaction;
      if (!transaction) return;

      try {
        options.upgrade(request.result, event.oldVersion, event.newVersion, transaction);
      } catch (error) {
        transaction.abort();
        reject(error);
      }
    };

    request.onsuccess = () => {
      resolve(new IDBPDatabase<TSchema>(request.result));
    };

    request.onerror = () => {
      reject(request.error ?? new Error(`Failed to open IndexedDB database '${name}'`));
    };
  });
}
